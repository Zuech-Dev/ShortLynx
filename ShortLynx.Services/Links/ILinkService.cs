using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Links;

public sealed record AnonymousLinkResult(LinkEntity Link, ShortCodeEntity ShortCode);

/// <summary>A recipient to mint a user-attributed code for: an opaque UserId plus an optional label.</summary>
public sealed record CodeRecipient(Guid UserId, string? Recipient = null);

public interface ILinkService
{
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, ApiKeyEntity owner, CancellationToken ct = default);

    /// <summary>Creates a user-owned link (admin dashboard); the link has no owning API key.</summary>
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, Guid userAccountId, CancellationToken ct = default);

    /// <summary>
    /// Creates a user-owned, user-attributed (Mode 2) link. The link gets no anonymous short code —
    /// it resolves only through the per-recipient codes minted via the recipient overload of
    /// <see cref="CreateUserLinkCodesAsync(Guid, IReadOnlyCollection{CodeRecipient}, bool, CancellationToken)"/>.
    /// </summary>
    Task<LinkEntity> CreateUserAttributedLinkAsync(string url, Guid userAccountId, CancellationToken ct = default);

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
}
