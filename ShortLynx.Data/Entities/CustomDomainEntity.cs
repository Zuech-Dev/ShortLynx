using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("CustomDomains")]
public class CustomDomainEntity
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string Domain { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public DomainVerificationStatus VerificationStatus { get; set; }
    public required string VerificationToken { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public virtual UserAccountEntity UserAccount { get; set; } = null!;
}
