using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Analytics;

/// <summary>A visit reduced to just the fields analytics aggregates over (no IP, no raw UA/referrer).</summary>
public readonly record struct VisitRow(
    string HashedIp,
    ClickSource Source,
    DeviceType Device,
    DateTimeOffset ClickedAt,
    string? Browser = null,
    string? Os = null,
    string? Country = null,
    string? Language = null,
    string? NavigationType = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null);

/// <summary>Clicks attributed to one platform (see <see cref="ClickSource"/>).</summary>
public sealed record SourceCount(string Source, long Count);

/// <summary>Clicks from one device class (see <see cref="DeviceType"/>).</summary>
public sealed record DeviceCount(string Device, long Count);

/// <summary>A generic labelled tally for the optional string dimensions (browser, OS, language, country).</summary>
public sealed record LabelCount(string Label, long Count);

/// <summary>Clicks on a single UTC calendar day, for the click-over-time series. <paramref name="UniqueCount"/>
/// is distinct hashed IPs that day (subject to the same hourly-rotation caveat as the overall unique count).</summary>
public sealed record DailyClicks(DateOnly Date, long Count, long UniqueCount);

/// <summary>Clicks that landed in one UTC hour-of-day bucket (0–23), across all days.</summary>
public sealed record HourlyClicks(int Hour, long Count);

/// <summary>The platform/device/time breakdown shared by link and campaign analytics. The browser/OS/
/// language/country/navigation tallies only count clicks where that dimension is known (nulls — e.g. under
/// a privacy signal — are dropped), so they may not sum to <see cref="TotalClicks"/>. Every dimension list
/// is k-anonymised: values with fewer than <see cref="ClickAggregator.AnonymityThreshold"/> clicks are
/// folded into an "Other" bucket so that low-frequency combinations can't single anyone out.</summary>
public sealed record ClickBreakdown(
    long TotalClicks,
    long UniqueClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline,
    IReadOnlyList<LabelCount> Browsers,
    IReadOnlyList<LabelCount> OperatingSystems,
    IReadOnlyList<LabelCount> Languages,
    IReadOnlyList<LabelCount> Countries,
    IReadOnlyList<LabelCount> NavigationTypes,
    // Operator-provided campaign labels off the inbound query string; k-anonymised like the rest.
    IReadOnlyList<LabelCount> UtmSources,
    IReadOnlyList<LabelCount> UtmMediums,
    IReadOnlyList<LabelCount> UtmCampaigns,
    IReadOnlyList<HourlyClicks> HourlyDistribution,
    long BotClicks,
    long HumanClicks,
    long HumanUniqueClicks);

/// <summary>
/// Reduces a set of visits to click totals plus platform/device/daily and browser/OS/language/country
/// breakdowns. Pure and in-memory: callers project visits (across one link or a whole campaign) into
/// <see cref="VisitRow"/>s first, so the same reduction serves the Core API and the Admin dashboard and
/// stays provider-agnostic (no DB date functions). <c>UniqueClicks</c> is distinct hashed IPs — note the
/// hash rotates hourly by design, so it dedupes within the hour rather than counting lifetime uniques.
/// </summary>
public static class ClickAggregator
{
    /// <summary>Dimension values seen fewer than this many times are folded into "Other" (k-anonymity,
    /// decided at k=10): in low-traffic contexts a rare browser/country/platform combination can narrow
    /// to an individual, so no breakdown — dashboard, API, or export — ever shows a bucket smaller than this.</summary>
    public const int AnonymityThreshold = 10;

    private const string OtherLabel = "Other";

    public static ClickBreakdown Summarize(IReadOnlyCollection<VisitRow> rows)
    {
        var sources = Fold(rows
                .GroupBy(r => r.Source)
                .Select(g => (Label: g.Key.ToString(), Count: g.LongCount())))
            .Select(x => new SourceCount(x.Label, x.Count))
            .ToList();

        var devices = Fold(rows
                .GroupBy(r => r.Device)
                .Select(g => (Label: g.Key.ToString(), Count: g.LongCount())))
            .Select(x => new DeviceCount(x.Label, x.Count))
            .ToList();

        // Bucket by UTC calendar day so the series is stable regardless of server/viewer timezone.
        var timeline = rows
            .GroupBy(r => DateOnly.FromDateTime(r.ClickedAt.UtcDateTime))
            .Select(g => new DailyClicks(g.Key, g.LongCount(), g.Select(r => r.HashedIp).Distinct().LongCount()))
            .OrderBy(t => t.Date)
            .ToList();

        // All 24 UTC hour buckets, zero-filled, so charts get an even axis without gap handling.
        var byHour = rows.GroupBy(r => r.ClickedAt.UtcDateTime.Hour).ToDictionary(g => g.Key, g => g.LongCount());
        var hourly = Enumerable.Range(0, 24).Select(h => new HourlyClicks(h, byHour.GetValueOrDefault(h))).ToList();

        var humanRows = rows.Where(r => r.Device != DeviceType.Bot).ToList();

        return new ClickBreakdown(
            TotalClicks: rows.Count,
            UniqueClicks: rows.Select(r => r.HashedIp).Distinct().Count(),
            FirstClickAt: rows.Count > 0 ? rows.Min(r => r.ClickedAt) : null,
            LastClickAt: rows.Count > 0 ? rows.Max(r => r.ClickedAt) : null,
            Sources: sources,
            Devices: devices,
            Timeline: timeline,
            Browsers: Tally(rows, r => r.Browser),
            OperatingSystems: Tally(rows, r => r.Os),
            Languages: Tally(rows, r => r.Language),
            Countries: Tally(rows, r => r.Country),
            NavigationTypes: Tally(rows, r => r.NavigationType),
            UtmSources: Tally(rows, r => r.UtmSource),
            UtmMediums: Tally(rows, r => r.UtmMedium),
            UtmCampaigns: Tally(rows, r => r.UtmCampaign),
            HourlyDistribution: hourly,
            BotClicks: rows.Count - humanRows.Count,
            HumanClicks: humanRows.Count,
            HumanUniqueClicks: humanRows.Select(r => r.HashedIp).Distinct().Count());
    }

    // Tallies a nullable string dimension, dropping nulls/blanks (e.g. unknown or privacy-suppressed),
    // busiest first, k-anonymised. Empty when nothing is known — callers hide the section in that case.
    private static List<LabelCount> Tally(IReadOnlyCollection<VisitRow> rows, Func<VisitRow, string?> select)
        => Fold(rows
                .Select(select)
                .Where(v => !string.IsNullOrEmpty(v))
                .GroupBy(v => v!)
                .Select(g => (Label: g.Key, Count: g.LongCount())))
            .Select(x => new LabelCount(x.Label, x.Count))
            .ToList();

    // k-anonymity fold: values below the threshold merge into a single "Other" bucket (joining an
    // existing "Other" if the dimension already produced one, e.g. ClickSource.Other). Ordered busiest
    // first with "Other" always last, so the suppressed remainder never masquerades as a real value.
    private static List<(string Label, long Count)> Fold(IEnumerable<(string Label, long Count)> items)
    {
        var list = items.ToList();
        var kept = new List<(string Label, long Count)>();
        long other = 0;

        foreach (var item in list)
        {
            if (item.Label == OtherLabel || item.Count < AnonymityThreshold)
                other += item.Count;
            else
                kept.Add(item);
        }

        kept = kept
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .ToList();
        if (other > 0)
            kept.Add((OtherLabel, other));
        return kept;
    }
}
