using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.UrlValidation;

namespace ShortLynx.Services.Links;

public sealed class LinkService(
    ShortLynxDbContext db,
    IShortCodeGenerator codeGenerator,
    IUrlValidationService urlValidator) : ILinkService
{
    private const int MaxCodeAttempts = 5;

    /// <summary>Creates an API-key-owned link (REST API path).</summary>
    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(
        string url, ApiKeyEntity owner, CancellationToken ct = default)
        => CreateLinkAsync(url, link => link.ApiKeyId = owner.Id, ct);

    /// <summary>Creates a user-owned link (admin dashboard path); leaves ApiKeyId null.</summary>
    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(
        string url, Guid userAccountId, CancellationToken ct = default)
        => CreateLinkAsync(url, link => link.UserAccountId = userAccountId, ct);

    private async Task<AnonymousLinkResult> CreateLinkAsync(
        string url, Action<LinkEntity> assignOwner, CancellationToken ct)
    {
        var validation = await urlValidator.ValidateAsync(url);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Reason, nameof(url));

        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(),
            OriginalUrl = url,
            Mode = LinkMode.Anonymous,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        assignOwner(link);
        db.LinkEntities.Add(link);
        await db.SaveChangesAsync(ct);

        return await MintShortCodeAsync(link, ct);
    }

    // Mints a unique short code for a freshly-created link, retrying on the (rare) code collision.
    private async Task<AnonymousLinkResult> MintShortCodeAsync(LinkEntity link, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxCodeAttempts; attempt++)
        {
            var code = codeGenerator.Generate(link.Id, userId: null, attempt);
            var shortCode = new ShortCodeEntity
            {
                Id = Guid.CreateVersion7(),
                LinkId = link.Id,
                Code = code,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            };
            db.ShortCodeEntities.Add(shortCode);
            try
            {
                await db.SaveChangesAsync(ct);
                return new AnonymousLinkResult(link, shortCode);
            }
            catch (DbUpdateException) when (attempt < MaxCodeAttempts)
            {
                db.ChangeTracker.Clear();
                // Re-attach the link so it isn't re-inserted on the next attempt.
                db.Attach(link);
            }
        }

        throw new InvalidOperationException("Failed to generate a unique short code after maximum attempts.");
    }

    public async Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IEnumerable<Guid> userIds, CancellationToken ct = default)
    {
        var results = new List<UserLinkCodeEntity>();

        foreach (var userId in userIds)
        {
            // Idempotency: return existing code rather than inserting a duplicate.
            var existing = await db.UserLinkCodeEntities
                .FirstOrDefaultAsync(c => c.LinkId == linkId && c.UserId == userId, ct);
            if (existing is not null)
            {
                results.Add(existing);
                continue;
            }

            for (var attempt = 0; attempt <= MaxCodeAttempts; attempt++)
            {
                var code = codeGenerator.Generate(linkId, userId, attempt);
                var entity = new UserLinkCodeEntity
                {
                    Id = Guid.CreateVersion7(),
                    LinkId = linkId,
                    UserId = userId,
                    Code = code,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                };
                db.UserLinkCodeEntities.Add(entity);
                try
                {
                    await db.SaveChangesAsync(ct);
                    results.Add(entity);
                    break;
                }
                catch (DbUpdateException) when (attempt < MaxCodeAttempts)
                {
                    db.ChangeTracker.Clear();
                }
            }
        }

        return results;
    }
}
