namespace ShortLynx.Services.Analytics;

/// <summary>One revealed (or suppressed, as "Other") city bucket in a link/campaign's breakdown.</summary>
public sealed record CityCount(string City, string? Country, long Count);

/// <summary>
/// Sums CityClickDailyEntity rows across dates into a per-(city, country) breakdown, suppressing any
/// bucket whose *unique-visitor* count falls short of the threshold. Deliberately separate from
/// ClickAggregator: city data comes pre-aggregated from a different table (see CityClickQueries), not
/// from raw VisitRows, and its k-anonymity is gated on unique visitors rather than raw clicks — a
/// single person refreshing the same link enough times must not be able to push a city over the line
/// by themselves, which ClickAggregator.Fold's click-count gate doesn't protect against.
/// </summary>
public static class CityAggregator
{
    /// <summary>k=6 unique visitors — CITY_GEO_PLAN.md §6.5, resolved lower than the site-wide k=10
    /// specifically because this dimension counts distinct visitors rather than raw clicks (a strictly
    /// stronger per-bucket guarantee than the other dimensions' click-count threshold provides).</summary>
    public const int AnonymityThreshold = 6;

    private const string OtherLabel = "Other";

    /// <param name="rows">Every daily row for the link(s) in scope, any number of dates per city.</param>
    /// <param name="anonymityThreshold">Overrides <see cref="AnonymityThreshold"/> — pass 0 to disable
    /// suppression (AnalyticsOptions.EnforceAnonymity's local-dev escape hatch, same convention as
    /// ClickAggregator.Summarize).</param>
    public static IReadOnlyList<CityCount> Summarize(
        IEnumerable<CityDailyRow> rows, int anonymityThreshold = AnonymityThreshold)
    {
        var byCity = rows
            .GroupBy(r => (r.City, r.Country))
            .Select(g => new
            {
                g.Key.City,
                g.Key.Country,
                Count = g.Sum(x => x.Count),
                // Summed across dates, not deduplicated across them -- consistent with how UniqueClicks
                // already works everywhere else in this system: the hash rotates daily, so "unique"
                // only ever means "within one rotation day" by design, never a lifetime count.
                UniqueCount = g.Sum(x => x.UniqueCount),
            })
            .ToList();

        var kept = new List<CityCount>();
        long other = 0;
        foreach (var c in byCity)
        {
            if (c.UniqueCount < anonymityThreshold)
                other += c.Count;
            else
                kept.Add(new CityCount(c.City, c.Country, c.Count));
        }

        kept = kept.OrderByDescending(c => c.Count).ThenBy(c => c.City, StringComparer.Ordinal).ToList();
        if (other > 0)
            kept.Add(new CityCount(OtherLabel, null, other));
        return kept;
    }
}
