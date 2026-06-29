using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

[Table("UserLinkCodes")]
public class UserLinkCodeEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public Guid UserId { get; set; }
    // unique index
    public string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsOneTimeUse { get; set; }
    public bool IsUsed { get; set; }
    // Optional human-readable label for the recipient (e.g. an email or campaign tag) shown in the
    // dashboard. Null for API-provisioned codes that are keyed only by UserId.
    public string? Recipient { get; set; }
    public virtual LinkEntity Link { get; set; } = null!;
}
