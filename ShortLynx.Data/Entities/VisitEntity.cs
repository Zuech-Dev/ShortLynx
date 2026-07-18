using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("Visits")]
public class VisitEntity
{
    public Guid Id { get; set; }

    // A visit belongs to exactly one of these: the link's shared code, or the code minted for one
    // social post. Both nullable because a row carries whichever it came in on — a post's clicks are
    // the same *fact* as a shared code's click, so they live in this table rather than a third
    // near-identical twin of it (UserVisits is already one). Which FK is set tells you the source.
    public Guid? ShortCodeId { get; set; }
    public Guid? SocialPostCodeId { get; set; }

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
    // IANA timezone (e.g. "America/Chicago") from GeoIP -- the only sub-country geo signal stored;
    // enables local-hour analysis without any coordinates (MASTER_PLAN P1).
    public string? TimeZone { get; set; }
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

    public virtual ShortCodeEntity? ShortCode { get; set; }
    public virtual SocialPostCodeEntity? SocialPostCode { get; set; }
}
