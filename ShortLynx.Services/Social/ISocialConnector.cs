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
}
