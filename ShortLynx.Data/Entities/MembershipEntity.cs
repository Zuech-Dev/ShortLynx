using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

/// <summary>Joins a <see cref="UserAccountEntity"/> to an <see cref="AccountEntity"/> with a role.</summary>
[Table("Memberships")]
public class MembershipEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserAccountId { get; set; }
    public AccountRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    // Audit only (which member sent the invite); not a foreign key, so it survives the inviter's deletion.
    public Guid? InvitedByUserAccountId { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual UserAccountEntity UserAccount { get; set; } = null!;
}
