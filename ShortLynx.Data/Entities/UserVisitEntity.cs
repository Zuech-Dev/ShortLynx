using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("UserVisits")]
public class UserVisitEntity
{
    public Guid Id { get; set; }
    public Guid UserLinkCodeId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public string HashedIp { get; set; } = null!;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }

    // Derived once at write time from Referrer/UserAgent (see SourceDetector).
    public ClickSource Source { get; set; }
    public DeviceType Device { get; set; }

    public virtual UserLinkCodeEntity UserLinkCode { get; set; } = null!;
}
