using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Credentials a user supplies to connect a social account. For Bluesky: handle (or email/DID) +
/// app password. For Mastodon: instance URL + access token. Never persisted — exchanged for tokens.
/// </summary>
public sealed record SocialCredentials(string Identifier, string Secret, string? InstanceUrl = null);

/// <summary>The verified identity + tokens a platform returns for a successful connection.</summary>
public sealed record SocialIdentity(
    string ExternalAccountId,
    string Handle,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);

/// <summary>Decrypted tokens handed to a connector for an API call. Never persisted in this form.</summary>
public sealed record SocialTokens(string AccessToken, string? RefreshToken);

/// <summary>What a connector needs to act as a connection: who + where + decrypted tokens.</summary>
public sealed record SocialConnectionContext(
    string ExternalAccountId,
    string Handle,
    string? InstanceUrl,
    SocialTokens Tokens);

/// <summary>A successfully published post: the platform's id plus a human-viewable URL when available.</summary>
public sealed record SocialPostRef(string ExternalPostId, string? PostUrl);

/// <summary>
/// Engagement metrics for a post. Fields the platform doesn't expose stay null — notably neither
/// Bluesky nor Mastodon reports impressions/views, so true CTR needs the gated platforms (Threads).
/// </summary>
public sealed record SocialPostMetrics(long? Impressions, long? Likes, long? Reposts, long? Replies);

/// <summary>
/// The platform said the access token is expired/invalid. The publish pipeline catches this, refreshes
/// via <see cref="ISocialConnector.RefreshAsync"/>, persists the rotated tokens, and retries once.
/// </summary>
public sealed class TokenExpiredException(string message) : Exception(message);

/// <summary>
/// One implementation per platform (Bluesky, Mastodon, …). Connectors talk to the platform's API;
/// everything above them (storage, encryption, account scoping) is platform-agnostic. Registered by
/// <see cref="SocialPlatform"/> and resolved from the DI set.
/// </summary>
public interface ISocialConnector
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// Validates the credentials against the platform and returns the verified identity + tokens.
    /// Throws <see cref="ArgumentException"/> when the platform rejects the credentials.
    /// </summary>
    Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default);

    /// <summary>
    /// Publishes a text post as the connected account. Throws <see cref="ArgumentException"/> for
    /// content the platform rejects (e.g. over the length limit) and <see cref="TokenExpiredException"/>
    /// when the access token is stale.
    /// </summary>
    Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default);

    /// <summary>
    /// Exchanges the refresh token for fresh tokens. Returns null when the platform has no refresh
    /// mechanism (Mastodon user tokens are long-lived) or no refresh token is held.
    /// </summary>
    Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default);

    /// <summary>
    /// Fetches current engagement metrics for a previously published post. Returns null when the post
    /// no longer exists (deleted on-platform). Throws <see cref="TokenExpiredException"/> on a stale
    /// access token, like <see cref="PublishAsync"/>.
    /// </summary>
    Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default);
}

/// <summary>
/// Additional capability for platforms that connect via a browser OAuth redirect (Threads) rather than
/// user-supplied credentials (Bluesky/Mastodon). The dashboard drives the redirect; this interface is
/// what the OAuth authorize/callback endpoints need from the connector.
/// </summary>
/// <summary>Resolves the OAuth-capable connector for a platform out of the registered connector set.</summary>
public static class OAuthConnectorResolver
{
    public static IOAuthSocialConnector Require(IEnumerable<ISocialConnector> connectors, SocialPlatform platform)
        => connectors.OfType<IOAuthSocialConnector>().FirstOrDefault(c => c.Platform == platform)
           ?? throw new InvalidOperationException($"No OAuth connector is registered for '{platform}'.");
}

public interface IOAuthSocialConnector : ISocialConnector
{
    /// <summary>Builds the URL to send the user's browser to, to grant this app access.</summary>
    string BuildAuthorizeUrl(string redirectUri, string state);

    /// <summary>
    /// Exchanges an authorization code (from the OAuth callback) for a verified identity + tokens.
    /// Throws <see cref="ArgumentException"/> when the platform rejects the code (expired, reused, or
    /// a redirect-URI mismatch).
    /// </summary>
    Task<SocialIdentity> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default);
}
