using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// City-level click counts, aggregated at ingest and never joined back to an individual visit — see
/// CITY_GEO_PLAN.md (ShortLynx.Hosted repo). Deliberately no navigation property to
/// <see cref="VisitEntity"/>/<see cref="UserVisitEntity"/> and no hashed IP here: the whole point of
/// this table is that a breach, export, or subpoena reaching it can never be joined to a click record
/// or a person. Only <see cref="LinkEntity"/> is a real FK, so deleting a link removes its aggregates.
///
/// <see cref="UniqueCount"/> is the number of distinct hashed-IP visitors that contributed to
/// <see cref="Count"/> that day — tracked via <see cref="CityClickDailyVisitorEntity"/> at write time,
/// consumed and then aggressively pruned (see VisitRetentionService), so the higher-sensitivity
/// per-visitor presence data does not linger once it has done its job. Reveal-gating uses
/// <see cref="UniqueCount"/>, never <see cref="Count"/> — a single person refreshing six times must
/// not be enough to reveal a city, which is why this column exists instead of reusing a plain count.
/// </summary>
[Table("CityClickDaily")]
public class CityClickDailyEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public required string City { get; set; }
    public string? Country { get; set; }
    public DateOnly Date { get; set; }
    public long Count { get; set; }
    public long UniqueCount { get; set; }

    public virtual LinkEntity Link { get; set; } = null!;
}
