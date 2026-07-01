using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.UrlValidation;

namespace ShortLynx.Services.Links;

public sealed class LinkService(
    ShortLynxDbContext db,
    IShortCodeGenerator codeGenerator,
    IUrlValidationService urlValidator,
    IEntitlements entitlements) : ILinkService
{
    private const int MaxCodeAttempts = 5;

    /// <summary>Creates an API-key-owned link (REST API path) for the key's account.</summary>
    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(
        string url, ApiKeyEntity owner, CancellationToken ct = default)
        => CreateLinkAsync(url, owner.AccountId, link => link.ApiKeyId = owner.Id, ct);

    /// <summary>Creates a link owned by an account (admin dashboard path).</summary>
    public Task<AnonymousLinkResult> CreateAnonymousLinkAsync(
        string url, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
        => CreateLinkAsync(url, accountId, link => link.UserAccountId = createdByUserAccountId, ct);

    /// <summary>Creates an account-owned, user-attributed (Mode 2) link with no anonymous short code.</summary>
    public async Task<LinkEntity> CreateUserAttributedLinkAsync(
        string url, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        if (!await entitlements.CanCreateLinkAsync(accountId, ct))
            throw new EntitlementException("Your plan's link limit has been reached.");
        if (!await entitlements.IsFeatureEnabledAsync(accountId, PlanFeature.UserAttributedLinks, ct))
            throw new EntitlementException("User-attributed links are not available on your plan.");

        var validation = await urlValidator.ValidateAsync(url);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Reason, nameof(url));

        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(),
            OriginalUrl = url,
            Mode = LinkMode.UserAttributed,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            AccountId = accountId,
            UserAccountId = createdByUserAccountId,
        };
        db.LinkEntities.Add(link);
        await db.SaveChangesAsync(ct);
        return link;
    }

    private async Task<AnonymousLinkResult> CreateLinkAsync(
        string url, Guid accountId, Action<LinkEntity> assignProvenance, CancellationToken ct)
    {
        if (!await entitlements.CanCreateLinkAsync(accountId, ct))
            throw new EntitlementException("Your plan's link limit has been reached.");

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
            AccountId = accountId,
        };
        assignProvenance(link);
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

    public async Task<bool> SetLinkDomainAsync(
        Guid linkId, Guid? customDomainId, Guid accountId, CancellationToken ct = default)
    {
        var link = await db.LinkEntities
            .FirstOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (link is null) return false;

        if (customDomainId is { } domainId)
        {
            var ownsVerified = await db.CustomDomainEntities.AnyAsync(
                d => d.Id == domainId
                  && d.AccountId == accountId
                  && d.VerificationStatus == DomainVerificationStatus.Verified, ct);
            if (!ownsVerified) return false;
        }

        link.CustomDomainId = customDomainId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetLinkCampaignAsync(
        Guid linkId, Guid? campaignId, Guid accountId, CancellationToken ct = default)
    {
        var link = await db.LinkEntities
            .FirstOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (link is null) return false;

        if (campaignId is { } cid)
        {
            var ownsCampaign = await db.CampaignEntities.AnyAsync(
                c => c.Id == cid && c.AccountId == accountId, ct);
            if (!ownsCampaign) return false;
        }

        link.CampaignId = campaignId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IEnumerable<Guid> userIds, CancellationToken ct = default)
        => CreateUserLinkCodesAsync(
            linkId, userIds.Select(id => new CodeRecipient(id)).ToList(), isOneTimeUse: false, ct);

    public async Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IReadOnlyCollection<CodeRecipient> recipients, bool isOneTimeUse, CancellationToken ct = default)
    {
        var results = new List<UserLinkCodeEntity>();

        // Pre-load existing labels for this link so re-submitting the same dashboard list is idempotent
        // even though each pasted recipient gets a fresh UserId.
        var labels = recipients
            .Select(r => r.Recipient)
            .Where(l => l is not null)
            .ToHashSet();
        var existingByLabel = labels.Count == 0
            ? new Dictionary<string, UserLinkCodeEntity>()
            : await db.UserLinkCodeEntities
                .Where(c => c.LinkId == linkId && c.Recipient != null && labels.Contains(c.Recipient))
                .ToDictionaryAsync(c => c.Recipient!, ct);

        foreach (var recipient in recipients)
        {
            // Dedupe by label (dashboard path) …
            if (recipient.Recipient is { } label && existingByLabel.TryGetValue(label, out var byLabel))
            {
                results.Add(byLabel);
                continue;
            }

            // … and by (linkId, userId) (API idempotency).
            var existing = await db.UserLinkCodeEntities
                .FirstOrDefaultAsync(c => c.LinkId == linkId && c.UserId == recipient.UserId, ct);
            if (existing is not null)
            {
                results.Add(existing);
                continue;
            }

            for (var attempt = 0; attempt <= MaxCodeAttempts; attempt++)
            {
                var code = codeGenerator.Generate(linkId, recipient.UserId, attempt);
                var entity = new UserLinkCodeEntity
                {
                    Id = Guid.CreateVersion7(),
                    LinkId = linkId,
                    UserId = recipient.UserId,
                    Code = code,
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                    IsOneTimeUse = isOneTimeUse,
                    Recipient = recipient.Recipient,
                };
                db.UserLinkCodeEntities.Add(entity);
                try
                {
                    await db.SaveChangesAsync(ct);
                    results.Add(entity);
                    if (recipient.Recipient is { } addedLabel)
                        existingByLabel[addedLabel] = entity;
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
