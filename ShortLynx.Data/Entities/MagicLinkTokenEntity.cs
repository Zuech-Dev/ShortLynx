using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

[Table("MagicLinkTokens")]
public class MagicLinkTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public virtual UserAccountEntity UserAccount { get; set; } = null!;
}
