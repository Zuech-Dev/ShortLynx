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
    string? Language = null);

/// <summary>Clicks attributed to one platform (see <see cref="ClickSource"/>).</summary>
public sealed record SourceCount(string Source, long Count);

/// <summary>Clicks from one device class (see <see cref="DeviceType"/>).</summary>
public sealed record DeviceCount(string Device, long Count);

/// <summary>A generic labelled tally for the optional string dimensions (browser, OS, language, country).</summary>
public sealed record LabelCount(string Label, long Count);

/// <summary>Clicks on a single UTC calendar day, for the click-over-time series. <paramref name="UniqueCount"/>
/// is distinct hashed IPs that day (subject to the same hourly-rotation caveat as the overall unique count).</summary>
public sealed record DailyClicks(DateOnly Date, long Count, long UniqueCount);

/// <summary>The platform/device/time breakdown shared by link and campaign analytics. The browser/OS/
/// language/country tallies only count clicks where that dimension is known (nulls — e.g. under a privacy
/// signal — are dropped), so they may not sum to <see cref="TotalClicks"/>.</summary>
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
    IReadOnlyList<LabelCount> Countries);

/// <summary>
/// Reduces a set of visits to click totals plus platform/device/daily and browser/OS/language/country
/// breakdowns. Pure and in-memory: callers project visits (across one link or a whole campaign) into
/// <see cref="VisitRow"/>s first, so the same reduction serves the Core API and the Admin dashboard and
/// stays provider-agnostic (no DB date functions). <c>UniqueClicks</c> is distinct hashed IPs — note the
/// hash rotates hourly by design, so it dedupes within the hour rather than counting lifetime uniques.
/// </summary>
public static class ClickAggregator
{
    public static ClickBreakdown Summarize(IReadOnlyCollection<VisitRow> rows)
    {
        var sources = rows
            .GroupBy(r => r.Source)
            .Select(g => new SourceCount(g.Key.ToString(), g.LongCount()))
            .OrderByDescending(s => s.Count)
            .ToList();

        var devices = rows
            .GroupBy(r => r.Device)
            .Select(g => new DeviceCount(g.Key.ToString(), g.LongCount()))
            .OrderByDescending(d => d.Count)
            .ToList();

        // Bucket by UTC calendar day so the series is stable regardless of server/viewer timezone.
        var timeline = rows
            .GroupBy(r => DateOnly.FromDateTime(r.ClickedAt.UtcDateTime))
            .Select(g => new DailyClicks(g.Key, g.LongCount(), g.Select(r => r.HashedIp).Distinct().LongCount()))
            .OrderBy(t => t.Date)
            .ToList();

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
            Countries: Tally(rows, r => r.Country));
    }

    // Tallies a nullable string dimension, dropping nulls/blanks (e.g. unknown or privacy-suppressed),
    // busiest first. Empty when nothing is known — callers hide the section in that case.
    private static List<LabelCount> Tally(IReadOnlyCollection<VisitRow> rows, Func<VisitRow, string?> select)
        => rows
            .Select(select)
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v!)
            .Select(g => new LabelCount(g.Key, g.LongCount()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .ToList();
}
