using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A social account an account has connected for publishing (and metrics). Account-scoped. Access/refresh
/// tokens are stored **encrypted at rest** (protected via ITokenProtector) — never in plaintext.
/// InstanceUrl is used by per-instance platforms (Mastodon); Bluesky leaves it null (or bsky.social).
/// </summary>
[Table("SocialConnections")]
public class SocialConnectionEntity
{
    public Guid Id { get; set; }

    /// <summary>The owning account. Connections scope by AccountId.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Audit only: the user who connected the account.</summary>
    public Guid? UserAccountId { get; set; }

    public SocialPlatform Platform { get; set; }

    /// <summary>The platform's own account id (e.g. a Bluesky DID) — stable across handle changes.</summary>
    public required string ExternalAccountId { get; set; }

    /// <summary>Human-friendly handle for display, e.g. "@me.bsky.social".</summary>
    public required string Handle { get; set; }

    /// <summary>Per-instance base URL for federated platforms (Mastodon); null for Bluesky.</summary>
    public string? InstanceUrl { get; set; }

    // Encrypted (ITokenProtector) — opaque ciphertext, never plaintext.
    public required string AccessTokenProtected { get; set; }
    public string? RefreshTokenProtected { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual UserAccountEntity? UserAccount { get; set; }
}
