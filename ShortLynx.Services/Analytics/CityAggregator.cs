namespace ShortLynx.Services.Analytics;

/// <summary>Tier at which a <see cref="CityCount"/> bucket was revealed — the city-geo cascade's rung.
/// A plain string on the wire, not a serialized enum: matches every sibling breakdown's convention
/// (e.g. ClickAggregator's SourceCount/DeviceCount), since ShortLynx.Core registers no global
/// JsonStringEnumConverter.</summary>
public static class CityGranularity
{
    public const string City = "City";
    public const string State = "State";
    public const string Country = "Country";
    public const string Other = "Other";
}

/// <summary>One revealed bucket in a link/campaign's geo breakdown, at whatever granularity it cleared
/// the anonymity bar. Only the fields for <see cref="Granularity"/>'s tier and above are populated —
/// e.g. a "State" row always has <see cref="City"/> null, and an "Other"/"Country" row has both
/// <see cref="City"/> and <see cref="State"/> null.</summary>
public sealed record CityCount(string? City, string? State, string? Country, long Count, string Granularity);

/// <summary>
/// Sums CityClickDailyEntity rows across dates into a geo breakdown, generalizing any bucket whose
/// *unique-visitor* count falls short of the threshold to a coarser tier instead of dropping it
/// outright: City → State → Country (revealed unconditionally) → "Other" (no country at all).
/// Deliberately separate from ClickAggregator: city data comes pre-aggregated from a different table
/// (see CityClickQueries), not from raw VisitRows, and its k-anonymity is gated on unique visitors
/// rather than raw clicks — a single person refreshing the same link enough times must not be able to
/// push a bucket over the line by themselves, which ClickAggregator.Fold's click-count gate doesn't
/// protect against.
///
/// The cascade runs entirely at read time, as three sequential regroups over the same rows, rather
/// than via a separately write-time-aggregated State table — see CITY_GEO_PLAN.md for why a parallel
/// State table can't cleanly reconcile against cities already revealed individually. One consequence
/// worth naming plainly (also documented there): folding several cities' UniqueCount together to test
/// the State tier is an *approximation*, the same kind of over-revealing bias this system already
/// accepts by summing UniqueCount across dates without a true re-dedup (the hashed-IP dedup table is
/// pruned after 2 days) — just now also applied across cities within a state, which mobile carrier
/// NAT/CGNAT can trigger without any repeat visit at all.
/// </summary>
public static class CityAggregator
{
    /// <summary>k=6 unique visitors — CITY_GEO_PLAN.md §6.5, resolved lower than the site-wide k=10
    /// specifically because this dimension counts distinct visitors rather than raw clicks (a strictly
    /// stronger per-bucket guarantee than the other dimensions' click-count threshold provides). Gates
    /// the City and State tiers; the Country tier is unconditional (see class remarks).</summary>
    public const int AnonymityThreshold = 6;

    /// <param name="rows">Every daily row for the link(s) in scope, any number of dates per city.</param>
    /// <param name="anonymityThreshold">Overrides <see cref="AnonymityThreshold"/> — pass 0 to disable
    /// suppression (AnalyticsOptions.EnforceAnonymity's local-dev escape hatch, same convention as
    /// ClickAggregator.Summarize). At 0, every row with a City clears the City tier immediately, so
    /// State/Country generalization never triggers.</param>
    public static IReadOnlyList<CityCount> Summarize(
        IEnumerable<CityDailyRow> rows, int anonymityThreshold = AnonymityThreshold)
    {
        var byCity = rows
            .GroupBy(r => (r.City, r.State, r.Country))
            .Select(g => new
            {
                g.Key.City,
                g.Key.State,
                g.Key.Country,
                // Summed across dates, not deduplicated across them -- consistent with how UniqueClicks
                // already works everywhere else in this system: the hash rotates daily, so "unique"
                // only ever means "within one rotation day" by design, never a lifetime count.
                Count = g.Sum(x => x.Count),
                UniqueCount = g.Sum(x => x.UniqueCount),
            })
            .ToList();

        var kept = new List<CityCount>();

        // Pass 1: city tier. Rows with no City at all (Country/State resolved, city empty -- common
        // for mobile/business IP blocks) skip straight to the leftover pool.
        var cityLeftover = new List<(string? State, string? Country, long Count, long UniqueCount)>();
        foreach (var c in byCity)
        {
            if (c.City is not null && c.UniqueCount >= anonymityThreshold)
                kept.Add(new CityCount(c.City, c.State, c.Country, c.Count, CityGranularity.City));
            else
                cityLeftover.Add((c.State, c.Country, c.Count, c.UniqueCount));
        }

        // Pass 2: state tier, folding together whatever didn't clear the city tier.
        var byState = cityLeftover
            .GroupBy(r => (r.State, r.Country))
            .Select(g => new
            {
                g.Key.State,
                g.Key.Country,
                Count = g.Sum(x => x.Count),
                UniqueCount = g.Sum(x => x.UniqueCount),
            })
            .ToList();

        var stateLeftover = new List<(string? Country, long Count)>();
        foreach (var s in byState)
        {
            if (s.State is not null && s.UniqueCount >= anonymityThreshold)
                kept.Add(new CityCount(null, s.State, s.Country, s.Count, CityGranularity.State));
            else
                stateLeftover.Add((s.Country, s.Count));
        }

        // Pass 3: country tier, revealed unconditionally -- matches the plain country breakdown
        // already shown to everyone elsewhere (gated only by ClickAggregator's separate raw-click
        // threshold, not by unique visitors here), and Count is exactly additive so no dedup concern
        // applies. Anything with no Country at all falls to Other.
        var byCountry = stateLeftover
            .GroupBy(r => r.Country)
            .Select(g => new { Country = g.Key, Count = g.Sum(x => x.Count) })
            .ToList();

        long other = 0;
        foreach (var c in byCountry)
        {
            if (c.Country is not null)
                kept.Add(new CityCount(null, null, c.Country, c.Count, CityGranularity.Country));
            else
                other += c.Count;
        }

        kept = kept
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.City, StringComparer.Ordinal)
            .ThenBy(c => c.State, StringComparer.Ordinal)
            .ThenBy(c => c.Country, StringComparer.Ordinal)
            .ToList();
        if (other > 0)
            kept.Add(new CityCount(null, null, null, other, CityGranularity.Other));
        return kept;
    }
}
