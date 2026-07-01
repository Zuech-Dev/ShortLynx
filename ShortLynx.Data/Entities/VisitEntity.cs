using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("Visits")]
public class VisitEntity
{
    public Guid Id { get; set; }
    public Guid ShortCodeId { get; set; }
    public DateTimeOffset ClickedAt { get; set; }
    public string HashedIp { get; set; } = null!;
    public string? Referrer { get; set; }
    public string? UserAgent { get; set; }

    // Derived once at write time from Referrer/UserAgent (see SourceDetector) so platform/device
    // breakdowns are a cheap GROUP BY instead of re-parsing strings on every analytics read.
    public ClickSource Source { get; set; }
    public DeviceType Device { get; set; }

    public virtual ShortCodeEntity ShortCode { get; set; } = null!;
}
