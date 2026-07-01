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

    // Low-entropy dimensions derived at write time from the raw request signals, which are then
    // discarded (see BackgroundVisitWriter): the raw UA + full referrer are fingerprinting vectors.
    // Nulls when the visitor sent a privacy signal (DNT / Sec-GPC) — the click still counts.
    public ClickSource Source { get; set; }
    public DeviceType Device { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? ReferrerHost { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public string? NavigationType { get; set; }

    public virtual ShortCodeEntity ShortCode { get; set; } = null!;
}
