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
    /// Publishes a link to each connection, minting a **distinct short code per post** so its clicks
    /// attribute exactly (rather than being guessed from a referrer that can't separate two posts on the
    /// same platform, and is absent for most app traffic). Each post's own URL is appended to
    /// <paramref name="text"/>; if the author already included a short URL of their own, their text is
    /// posted verbatim and that post falls back to referrer attribution.
    ///
    /// <paramref name="publicBaseUrl"/> is the deployment's public short-link base (a verified pinned
    /// custom domain still wins per <c>ShortUrlBuilder</c>) — the caller supplies it because the config
    /// key differs per host app. Throws <see cref="EntitlementException"/> when the plan gates
    /// publishing; per-connection problems are reported in the results, not thrown.
    /// </summary>
    Task<IReadOnlyList<PublishResult>> PublishLinkAsync(
        Guid accountId, Guid linkId, IReadOnlyCollection<Guid> connectionIds,
        string? text, string? publicBaseUrl, CancellationToken ct = default);
}
