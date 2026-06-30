using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;

namespace ShortLynx.Core.Analytics;

/// <summary>A visit reduced to just the fields analytics aggregates over (no IP, no raw UA/referrer).</summary>
internal readonly record struct VisitRow(string HashedIp, ClickSource Source, DeviceType Device, DateTimeOffset ClickedAt);

/// <summary>The platform/device/time breakdown shared by link and campaign analytics.</summary>
internal sealed record ClickBreakdown(
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
/// the same reduction serves both surfaces and stays provider-agnostic (no DB date functions).
/// </summary>
internal static class ClickAggregator
{
    internal static ClickBreakdown Summarize(IReadOnlyCollection<VisitRow> rows)
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
