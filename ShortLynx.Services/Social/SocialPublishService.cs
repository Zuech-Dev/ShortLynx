using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Services.Social;

public sealed class SocialPublishService(
    ShortLynxDbContext db,
    IEnumerable<ISocialConnector> connectors,
    ITokenProtector protector,
    IEntitlements entitlements) : ISocialPublishService
{
    public async Task<IReadOnlyList<PublishResult>> PublishLinkAsync(
        Guid accountId, Guid linkId, IReadOnlyCollection<Guid> connectionIds,
        string? text, string shortUrl, CancellationToken ct = default)
    {
        if (!await entitlements.IsFeatureEnabledAsync(accountId, PlanFeature.SocialPublishing, ct))
            throw new EntitlementException("Social publishing is not available on your plan.");

        var linkOwned = await db.LinkEntities.AnyAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (!linkOwned)
            throw new ArgumentException("Link not found in this account.", nameof(linkId));

        var composed = Compose(text, shortUrl);
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

            results.Add(await PublishToConnectionAsync(connection, linkId, composed, ct));
        }

        return results;
    }

    private async Task<PublishResult> PublishToConnectionAsync(
        SocialConnectionEntity connection, Guid linkId, string composed, CancellationToken ct)
    {
        var connector = connectors.FirstOrDefault(c => c.Platform == connection.Platform);
        if (connector is null)
            return new PublishResult(connection.Id, connection.Handle, false, null,
                $"No connector is available for '{connection.Platform}'.");

        try
        {
            var context = BuildContext(connection);
            SocialPostRef postRef;
            try
            {
                postRef = await connector.PublishAsync(context, composed, ct);
            }
            catch (TokenExpiredException)
            {
                // Stale access token: refresh, persist the rotated pair, retry exactly once.
                var rotated = await connector.RefreshAsync(context, ct);
                if (rotated is null)
                    return new PublishResult(connection.Id, connection.Handle, false, null,
                        "The connection's tokens have expired — reconnect the account.");

                connection.AccessTokenProtected = protector.Protect(rotated.AccessToken);
                connection.RefreshTokenProtected = rotated.RefreshToken is null ? null : protector.Protect(rotated.RefreshToken);
                await db.SaveChangesAsync(ct);

                postRef = await connector.PublishAsync(BuildContext(connection), composed, ct);
            }

            var post = new SocialPostEntity
            {
                Id = Guid.CreateVersion7(),
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
            await db.SaveChangesAsync(ct);

            return new PublishResult(connection.Id, connection.Handle, true, post, null);
        }
        catch (TokenExpiredException)
        {
            return new PublishResult(connection.Id, connection.Handle, false, null,
                "The connection's tokens have expired — reconnect the account.");
        }
        catch (ArgumentException ex)
        {
            return new PublishResult(connection.Id, connection.Handle, false, null, ex.Message);
        }
        catch (HttpRequestException)
        {
            return new PublishResult(connection.Id, connection.Handle, false, null,
                "The platform could not be reached. Try again shortly.");
        }
    }

    private SocialConnectionContext BuildContext(SocialConnectionEntity connection) => new(
        connection.ExternalAccountId,
        connection.Handle,
        connection.InstanceUrl,
        new SocialTokens(
            protector.Unprotect(connection.AccessTokenProtected),
            connection.RefreshTokenProtected is null ? null : protector.Unprotect(connection.RefreshTokenProtected)));

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
