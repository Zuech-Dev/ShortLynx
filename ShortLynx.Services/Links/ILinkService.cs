using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Links;

public sealed record AnonymousLinkResult(LinkEntity Link, ShortCodeEntity ShortCode);

/// <summary>A recipient to mint a user-attributed code for: an opaque UserId plus an optional label.</summary>
public sealed record CodeRecipient(Guid UserId, string? Recipient = null);

public interface ILinkService
{
    /// <summary>
    /// API-key path. Optional <paramref name="customCode"/> mints an operator-chosen (vanity) code
    /// instead of a random one — entitlement- and format-gated; throws <see cref="Entitlements.EntitlementException"/>,
    /// <see cref="ArgumentException"/> (invalid), or <see cref="ShortCodes.CustomCodeTakenException"/> (409).
    /// </summary>
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, ApiKeyEntity owner, string? customCode = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a link owned by an account (admin dashboard); no owning API key. Optionally assigns it to
    /// one of the account's campaigns at creation — throws <see cref="ArgumentException"/> if the campaign
    /// isn't the account's. Optional <paramref name="customCode"/> mints a vanity code (see the API-key
    /// overload for the failure modes).
    /// </summary>
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, Guid accountId, Guid? createdByUserAccountId = null, Guid? campaignId = null, string? customCode = null, CancellationToken ct = default);

    /// <summary>
    /// Creates an account-owned, user-attributed (Mode 2) link. The link gets no anonymous short code —
    /// it resolves only through the per-recipient codes minted via the recipient overload of
    /// <see cref="CreateUserLinkCodesAsync(Guid, IReadOnlyCollection{CodeRecipient}, bool, CancellationToken)"/>.
    /// Optionally assigns it to one of the account's campaigns at creation.
    /// </summary>
    Task<LinkEntity> CreateUserAttributedLinkAsync(string url, Guid accountId, Guid? createdByUserAccountId = null, Guid? campaignId = null, CancellationToken ct = default);

    /// <summary>
    /// Bulk-mints one UserLinkCode per userId. Idempotent: returns the existing code
    /// if one already exists for a (linkId, userId) pair.
    /// </summary>
    Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IEnumerable<Guid> userIds, CancellationToken ct = default);

    /// <summary>
    /// Bulk-mints one UserLinkCode per recipient, stamping the optional label and one-time-use flag.
    /// Idempotent by (linkId, userId); additionally dedupes by (linkId, recipient label) when a label
    /// is supplied, so re-submitting the same dashboard list doesn't create duplicates.
    /// </summary>
    Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IReadOnlyCollection<CodeRecipient> recipients, bool isOneTimeUse, CancellationToken ct = default);

    /// <summary>
    /// Pins (or, with null, unpins) an account's link to one of the account's verified custom domains.
    /// Returns false if the link isn't the account's, or the domain isn't the account's and verified.
    /// </summary>
    Task<bool> SetLinkDomainAsync(Guid linkId, Guid? customDomainId, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Assigns (or, with null, unassigns) an account's link to one of the account's campaigns.
    /// Returns false if the link isn't the account's, or the campaign isn't the account's.
    /// </summary>
    Task<bool> SetLinkCampaignAsync(Guid linkId, Guid? campaignId, Guid accountId, CancellationToken ct = default);
}
