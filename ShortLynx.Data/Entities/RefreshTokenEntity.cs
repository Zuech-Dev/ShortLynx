using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A long-lived, revocable refresh token issued alongside a JWT access token. The opaque token is
/// stored only as a hash; tokens are rotated on use (the old one is revoked and points at its
/// replacement, enabling reuse detection).
/// </summary>
[Table("RefreshTokens")]
public class RefreshTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The token this one was rotated into (set when revoked by a refresh).</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public virtual UserAccountEntity UserAccount { get; set; } = null!;
}
