using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Links;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Services.Social;

public sealed class SocialPublishService(
    ShortLynxDbContext db,
    IEnumerable<ISocialConnector> connectors,
    ITokenProtector protector,
    IShortCodeGenerator codeGenerator,
    IEntitlements entitlements) : ISocialPublishService
{
    private const int MaxCodeAttempts = 5;

    public async Task<IReadOnlyList<PublishResult>> PublishLinkAsync(
        Guid accountId, Guid linkId, IReadOnlyCollection<Guid> connectionIds,
        string? text, string? publicBaseUrl, CancellationToken ct = default)
    {
        if (!await entitlements.IsFeatureEnabledAsync(accountId, PlanFeature.SocialPublishing, ct))
            throw new EntitlementException("Social publishing is not available on your plan.");

        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (link is null)
            throw new ArgumentException("Link not found in this account.", nameof(linkId));

        // If the author already put a short URL in the text themselves, respect it: don't mint a
        // per-post code or append a second URL. Those posts lose exact attribution and fall back to
        // referrer-based ClickSource — an honest trade for not mangling what they wrote.
        var authorSuppliedUrl = ContainsShortUrl(text, publicBaseUrl);
        var results = new List<PublishResult>(connectionIds.Count);

        foreach (var connectionId in connectionIds.Distinct())
        {
            var connection = await db.SocialConnectionEntities
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.AccountId == accountId, ct);
            if (connection is null)
            {
                results.Add(new PublishResult(connectionId, "?", false, null, "Connection not found."));
                continue;
            }

            results.Add(await PublishToConnectionAsync(connection, link, text, publicBaseUrl, authorSuppliedUrl, ct));
        }

        return results;
    }

    private async Task<PublishResult> PublishToConnectionAsync(
        SocialConnectionEntity connection, LinkEntity link, string? text,
        string? publicBaseUrl, bool authorSuppliedUrl, CancellationToken ct)
    {
        var linkId = link.Id;
        var connector = connectors.FirstOrDefault(c => c.Platform == connection.Platform);
        if (connector is null)
            return new PublishResult(connection.Id, connection.Handle, false, null,
                $"No connector is available for '{connection.Platform}'.");

        // The post id is minted up front so it can seed its own attribution code (the code's value is
        // deterministic in the post id), but the code row is saved with a null SocialPostId — the post
        // it points at doesn't exist until after publishing succeeds. We fill that link in at the end.
        var postId = Guid.CreateVersion7();
        SocialPostCodeEntity? postCode = null;
        string composed;
        try
        {
            if (authorSuppliedUrl)
            {
                composed = text!.Trim();
            }
            else
            {
                postCode = await MintPostCodeAsync(linkId, postId, ct);
                var postUrl = await ShortUrlBuilder.BuildAsync(db, link, postCode.Code, publicBaseUrl, ct);
                composed = Compose(text, postUrl);
            }
        }
        catch (InvalidOperationException ex)
        {
            return new PublishResult(connection.Id, connection.Handle, false, null, ex.Message);
        }

        try
        {
            // Stale-token refresh + single retry is handled centrally by ConnectorTokenGuard.
            var postRef = await ConnectorTokenGuard.ExecuteAsync(
                db, protector, connector, connection,
                context => connector.PublishAsync(context, composed, ct), ct);

            var post = new SocialPostEntity
            {
                Id = postId,
                AccountId = connection.AccountId,
                LinkId = linkId,
                SocialConnectionId = connection.Id,
                Platform = connection.Platform,
                Handle = connection.Handle,
                ExternalPostId = postRef.ExternalPostId,
                PostUrl = postRef.PostUrl,
                Text = composed,
                PostedAt = DateTimeOffset.UtcNow,
            };
            db.SocialPostEntities.Add(post);
            if (postCode is not null) postCode.SocialPostId = post.Id; // now the post exists, point the code at it
            await db.SaveChangesAsync(ct);

            return new PublishResult(connection.Id, connection.Handle, true, post, null);
        }
        catch (TokenExpiredException ex)
        {
            return await FailAsync(connection, postCode, ex.Message, ct);
        }
        catch (ArgumentException ex)
        {
            return await FailAsync(connection, postCode, ex.Message, ct);
        }
        catch (HttpRequestException)
        {
            return await FailAsync(connection, postCode,
                "The platform could not be reached. Try again shortly.", ct);
        }
    }

    // A code minted for a post that never published would otherwise linger as a live, resolvable code
    // attached to no post — clutter in the link's code list that can never be attributed.
    private async Task<PublishResult> FailAsync(
        SocialConnectionEntity connection, SocialPostCodeEntity? postCode, string error, CancellationToken ct)
    {
        if (postCode is not null)
        {
            db.SocialPostCodeEntities.Remove(postCode);
            await db.SaveChangesAsync(ct);
        }
        return new PublishResult(connection.Id, connection.Handle, false, null, error);
    }

    // Mints this post's own attribution code (a SocialPostCode — many per link, unlike the link's single
    // shared ShortCode). The post id is the discriminator, so codes are distinct by construction rather
    // than by exhausting collision attempts. SocialPostId stays null until the post is created.
    private async Task<SocialPostCodeEntity> MintPostCodeAsync(Guid linkId, Guid postId, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxCodeAttempts; attempt++)
        {
            var postCode = new SocialPostCodeEntity
            {
                Id = Guid.CreateVersion7(),
                LinkId = linkId,
                Code = codeGenerator.Generate(linkId, discriminator: postId, attempt),
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            };
            db.SocialPostCodeEntities.Add(postCode);
            try
            {
                await db.SaveChangesAsync(ct);
                return postCode;
            }
            catch (DbUpdateException) when (attempt < MaxCodeAttempts)
            {
                db.Entry(postCode).State = EntityState.Detached; // retry with a fresh candidate
            }
        }

        throw new InvalidOperationException("Failed to generate a unique short code for the post.");
    }

    // Does the author's own text already carry a short URL for this deployment? If so we leave it alone
    // rather than appending a second one.
    internal static bool ContainsShortUrl(string? text, string? publicBaseUrl)
        => !string.IsNullOrWhiteSpace(text)
           && !string.IsNullOrWhiteSpace(publicBaseUrl)
           && text.Contains(publicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    // The tracked short URL is the point of the post — append it unless the author already placed it.
    internal static string Compose(string? text, string shortUrl)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return shortUrl;
        return trimmed.Contains(shortUrl, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}\n\n{shortUrl}";
    }
}
