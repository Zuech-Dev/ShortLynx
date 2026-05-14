namespace ShortLynx.Data.Entities;

public class UserLinkCodeEntity
{
    // PK, ValueGeneratedNever
    public Guid Id { get; set; }
    // FK -> Link
    public Guid LinkId { get; set; }
    // external user ID - no FK, not managed by ShortLynx
    public Guid? UserId { get; set; } = null;
    // unique index
    public string Code { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsOneTimeUse { get; set; }
    public bool IsUsed { get; set; }
    public virtual LinkEntity Link { get; set; } = null!;
    // public virtual UserEntity? User { get; set; }
}
