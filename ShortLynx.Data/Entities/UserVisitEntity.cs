namespace ShortLynx.Data.Entities;

public class UserVisitEntity
{
    public Guid Id { get; set; }
    public Guid UserLinkCodeId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public string HashedIp { get; set; } = null!;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }
    public virtual LinkEntity Link { get; set; } = null!;
}
