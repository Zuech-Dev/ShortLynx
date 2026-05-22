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
    public virtual LinkEntity Link { get; set; } = null!;
}
