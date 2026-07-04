using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Social;

/// <summary>Outcome of publishing to one connection. Partial failure is normal — each target reports its own.</summary>
public sealed record PublishResult(
    Guid ConnectionId,
    string Handle,
    bool Success,
    SocialPostEntity? Post,
    string? Error);

/// <summary>
/// Publishes a link to one or more of the account's connected social accounts and records a
/// <see cref="SocialPostEntity"/> per successful post. Handles token expiry transparently: on a stale
/// access token it refreshes via the connector, persists the rotated tokens, and retries once.
/// </summary>
public interface ISocialPublishService
{
    /// <summary>
    /// Composes the post (appends <paramref name="shortUrl"/> unless the text already contains it) and
    /// publishes it to each connection. Throws <see cref="EntitlementException"/> when the plan gates
    /// publishing; per-connection problems are reported in the results, not thrown.
    /// </summary>
    Task<IReadOnlyList<PublishResult>> PublishLinkAsync(
        Guid accountId, Guid linkId, IReadOnlyCollection<Guid> connectionIds,
        string? text, string shortUrl, CancellationToken ct = default);
}
