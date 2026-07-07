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

    // Low-entropy dimensions derived at write time; raw UA/referrer are discarded. Null under a
    // privacy signal (DNT / Sec-GPC).
    public ClickSource Source { get; set; }
    public DeviceType Device { get; set; }
    public string? Browser { get; set; }
    public string? Os { get; set; }
    public string? ReferrerHost { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; }
    public string? NavigationType { get; set; }

    // UTM tags from the inbound short-link query string (?utm_source=...), parsed and truncated at
    // write time. Operator-provided campaign labels, not visitor signals -- but still nulled under a
    // privacy signal like every other dimension.
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }

    public virtual UserLinkCodeEntity UserLinkCode { get; set; } = null!;
}
