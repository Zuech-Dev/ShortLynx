using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Analytics;

/// <summary>A visit reduced to just the fields analytics aggregates over (no IP, no raw UA/referrer).</summary>
public readonly record struct VisitRow(string HashedIp, ClickSource Source, DeviceType Device, DateTimeOffset ClickedAt);

/// <summary>Clicks attributed to one platform (see <see cref="ClickSource"/>).</summary>
public sealed record SourceCount(string Source, long Count);

/// <summary>Clicks from one device class (see <see cref="DeviceType"/>).</summary>
public sealed record DeviceCount(string Device, long Count);

/// <summary>Clicks on a single UTC calendar day, for the click-over-time series.</summary>
public sealed record DailyClicks(DateOnly Date, long Count);

/// <summary>The platform/device/time breakdown shared by link and campaign analytics.</summary>
public sealed record ClickBreakdown(
    long TotalClicks,
    long UniqueClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline);

/// <summary>
/// Reduces a set of visits to click totals plus platform/device/daily breakdowns. Pure and in-memory:
/// callers project visits (across one link or a whole campaign) into <see cref="VisitRow"/>s first, so
/// the same reduction serves the Core API and the Admin dashboard and stays provider-agnostic (no DB
/// date functions). <c>UniqueClicks</c> is distinct hashed IPs — note the hash rotates hourly by design,
/// so it dedupes within the hour rather than counting lifetime-unique visitors.
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
            .Select(g => new DailyClicks(g.Key, g.LongCount()))
            .OrderBy(t => t.Date)
            .ToList();

        return new ClickBreakdown(
            TotalClicks: rows.Count,
            UniqueClicks: rows.Select(r => r.HashedIp).Distinct().Count(),
            FirstClickAt: rows.Count > 0 ? rows.Min(r => r.ClickedAt) : null,
            LastClickAt: rows.Count > 0 ? rows.Max(r => r.ClickedAt) : null,
            Sources: sources,
            Devices: devices,
            Timeline: timeline);
    }
}
