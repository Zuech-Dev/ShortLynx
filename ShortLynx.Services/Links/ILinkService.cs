using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Links;

public sealed record AnonymousLinkResult(LinkEntity Link, ShortCodeEntity ShortCode);

public interface ILinkService
{
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, ApiKeyEntity owner, CancellationToken ct = default);

    /// <summary>Creates a user-owned link (admin dashboard); the link has no owning API key.</summary>
    Task<AnonymousLinkResult> CreateAnonymousLinkAsync(string url, Guid userAccountId, CancellationToken ct = default);

    /// <summary>
    /// Bulk-mints one UserLinkCode per userId. Idempotent: returns the existing code
    /// if one already exists for a (linkId, userId) pair.
    /// </summary>
    Task<IReadOnlyList<UserLinkCodeEntity>> CreateUserLinkCodesAsync(
        Guid linkId, IEnumerable<Guid> userIds, CancellationToken ct = default);
}
