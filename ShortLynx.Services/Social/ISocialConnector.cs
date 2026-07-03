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
}
