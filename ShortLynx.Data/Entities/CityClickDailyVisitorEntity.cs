using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A presence marker — "this hashed IP visited this city on this day" — used only to compute
/// <see cref="CityClickDailyEntity.UniqueCount"/> correctly (distinct visitors, not raw clicks). Not
/// part of the original CITY_GEO_PLAN.md schema: it exists specifically to support a k-anonymity
/// threshold measured in unique visitors rather than clicks, which the plan's simple counter column
/// can't do on its own -- a single person double-clicking or refreshing must not be able to push a
/// city over the reveal threshold by themselves.
///
/// Carries the same daily-rotating hash already used everywhere else (never raw), no device/browser/
/// OS, no click timestamp, and — like <see cref="CityClickDailyEntity"/> — no FK to an actual visit
/// row. It is still the more sensitive of the two tables (a per-city set of hashed IPs, even
/// de-identified), so it is retained far more briefly: pruned after a couple of days once each date's
/// count has been finalized into <see cref="CityClickDailyEntity"/>, rather than riding the 90-day
/// sweep that table gets. See VisitRetentionService.
/// </summary>
[Table("CityClickDailyVisitors")]
public class CityClickDailyVisitorEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public required string City { get; set; }
    // Carried even though it's "redundant" with City for most cities: city names collide across
    // countries (Paris, France vs. Paris, Texas; Cambridge, UK vs. Cambridge, MA), so City alone is not
    // a safe dedupe key. Must match CityClickDailyEntity's keying exactly, or uniqueness silently
    // undercounts or overcounts across a same-named city in two countries.
    public string? Country { get; set; }
    public DateOnly Date { get; set; }
    public required string HashedIp { get; set; }
}
