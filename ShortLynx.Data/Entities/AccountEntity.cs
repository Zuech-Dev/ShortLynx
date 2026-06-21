using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A workspace/tenant that owns resources (links, custom domains, API keys). Users access an account
/// through a <see cref="MembershipEntity"/> carrying a role.
/// </summary>
[Table("Accounts")]
public class AccountEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<MembershipEntity> Memberships { get; set; } = [];
}
