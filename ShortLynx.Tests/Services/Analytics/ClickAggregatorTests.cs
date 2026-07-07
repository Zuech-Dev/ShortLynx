using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class ClickAggregatorTests
{
    private static VisitRow Row(string ip, string? browser = null, string? os = null,
        string? country = null, string? language = null, string? navigationType = null,
        DeviceType device = DeviceType.Desktop, ClickSource source = ClickSource.Direct,
        DateTimeOffset? clickedAt = null)
        => new(ip, source, device, clickedAt ?? DateTimeOffset.UtcNow, browser, os, country, language, navigationType);

    // N rows with distinct IPs sharing one dimension value, to push a bucket over/under the k threshold.
    private static IEnumerable<VisitRow> Many(int n, Func<int, VisitRow> make)
        => Enumerable.Range(0, n).Select(make);

    [Fact]
    public void Summarize_TalliesStringDimensions_DroppingNulls()
    {
        // Chrome and "en" clear the k=10 threshold; the null-browser/null-language row is dropped
        // from those tallies entirely (privacy-suppressed rows must not appear anywhere).
        var rows =
            Many(10, i => Row($"c{i}", browser: "Chrome", language: "en"))
            .Append(Row("x1", browser: null, language: null))
            .ToList();

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(11, b.TotalClicks);
        Assert.Equal("Chrome", Assert.Single(b.Browsers).Label);
        Assert.Equal(10, b.Browsers[0].Count);
        Assert.Equal(10, b.Languages.Single(l => l.Label == "en").Count);

        // Country was never set on any row → empty (so the UI hides the section until GeoIP lands).
        Assert.Empty(b.Countries);
        Assert.Empty(b.OperatingSystems);
        Assert.Empty(b.NavigationTypes);
    }

    [Fact]
    public void Summarize_FoldsValuesBelowAnonymityThresholdIntoOther()
    {
        // Firefox (12) survives; Safari (3) and Opera (2) fold into "Other" (5) — never shown alone,
        // because a 3-click browser bucket on a low-traffic link can narrow to an individual.
        var rows =
            Many(12, i => Row($"f{i}", browser: "Firefox"))
            .Concat(Many(3, i => Row($"s{i}", browser: "Safari")))
            .Concat(Many(2, i => Row($"o{i}", browser: "Opera")))
            .ToList();

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(2, b.Browsers.Count);
        Assert.Equal(new LabelCount("Firefox", 12), b.Browsers[0]);
        Assert.Equal(new LabelCount("Other", 5), b.Browsers[1]);
    }

    [Fact]
    public void Summarize_MergesSuppressedSourcesIntoExistingOtherBucket()
    {
        // ClickSource.Other already exists as a real value; suppressed platforms must join it,
        // not produce a second "Other" row. Reddit (2) + Other (10) → Other (12).
        var rows =
            Many(15, i => Row($"d{i}", source: ClickSource.Direct))
            .Concat(Many(10, i => Row($"x{i}", source: ClickSource.Other)))
            .Concat(Many(2, i => Row($"r{i}", source: ClickSource.Reddit)))
            .ToList();

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(2, b.Sources.Count);
        Assert.Equal(new SourceCount("Direct", 15), b.Sources[0]);
        Assert.Equal(new SourceCount("Other", 12), b.Sources[1]);
        // "Other" is always last so the suppressed remainder never masquerades as a leading platform.
        Assert.Equal("Other", b.Sources[^1].Source);
    }

    [Fact]
    public void Summarize_SplitsBotAndHumanClicks()
    {
        var rows =
            Many(11, i => Row($"h{i}"))
            .Concat(Many(4, i => Row($"b{i}", device: DeviceType.Bot)))
            // A human re-click within the same hash window: 12 human clicks, 11 unique.
            .Append(Row("h0"))
            .ToList();

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(16, b.TotalClicks);
        Assert.Equal(12, b.HumanClicks);
        Assert.Equal(4, b.BotClicks);
        Assert.Equal(11, b.HumanUniqueClicks);
        Assert.Equal(b.TotalClicks, b.HumanClicks + b.BotClicks);
    }

    [Fact]
    public void Summarize_BuildsZeroFilledHourlyDistribution()
    {
        var at9 = new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero);
        var at21 = new DateTimeOffset(2026, 7, 2, 21, 5, 0, TimeSpan.Zero); // different day, same hour buckets
        var rows = new[]
        {
            Row("a", clickedAt: at9),
            Row("b", clickedAt: at9),
            Row("c", clickedAt: at21),
        };

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(24, b.HourlyDistribution.Count);
        Assert.Equal(2, b.HourlyDistribution[9].Count);
        Assert.Equal(1, b.HourlyDistribution[21].Count);
        Assert.Equal(3, b.HourlyDistribution.Sum(h => h.Count));
        Assert.All(b.HourlyDistribution.Select((h, i) => (h.Hour, i)), x => Assert.Equal(x.i, x.Hour));
    }

    [Fact]
    public void Summarize_LocalHourlyDistribution_UsesVisitorTimezone()
    {
        // 14:00 UTC is 09:00 in America/Chicago (CDT, UTC-5, July). Rows without a stored zone
        // (e.g. GeoIP off or privacy signal) are excluded from the local series, never guessed.
        var at = new DateTimeOffset(2026, 7, 1, 14, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Row("a", clickedAt: at) with { TimeZone = "America/Chicago" },
            Row("b", clickedAt: at) with { TimeZone = "America/Chicago" },
            Row("c", clickedAt: at), // no zone
            Row("d", clickedAt: at) with { TimeZone = "Not/AZone" }, // unknown zone dropped
        };

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(4, b.HourlyDistribution[14].Count);      // UTC series counts everything
        Assert.Equal(2, b.LocalHourlyDistribution[9].Count);  // local series only the zoned rows
        Assert.Equal(2, b.LocalHourlyDistribution.Sum(h => h.Count));
    }

    [Fact]
    public void Summarize_TalliesNavigationTypes()
    {
        var rows =
            Many(10, i => Row($"n{i}", navigationType: "cross-site"))
            .Concat(Many(10, i => Row($"m{i}", navigationType: "none")))
            .ToList();

        var b = ClickAggregator.Summarize(rows);

        Assert.Equal(2, b.NavigationTypes.Count);
        Assert.Equal(10, b.NavigationTypes.Single(n => n.Label == "cross-site").Count);
    }

    [Fact]
    public void Summarize_EmptyInput_YieldsEmptyBreakdown()
    {
        var b = ClickAggregator.Summarize([]);

        Assert.Equal(0, b.TotalClicks);
        Assert.Equal(0, b.HumanClicks);
        Assert.Equal(0, b.BotClicks);
        Assert.Null(b.FirstClickAt);
        Assert.Empty(b.Sources);
        Assert.Equal(24, b.HourlyDistribution.Count);
        Assert.All(b.HourlyDistribution, h => Assert.Equal(0, h.Count));
    }
}
