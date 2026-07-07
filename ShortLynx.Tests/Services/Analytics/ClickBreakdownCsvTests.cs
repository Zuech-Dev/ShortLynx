using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class ClickBreakdownCsvTests
{
    private static VisitRow Row(string ip, string? utmCampaign = null, DeviceType device = DeviceType.Desktop)
        => new(ip, ClickSource.Direct, device, new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            UtmCampaign: utmCampaign);

    [Fact]
    public void Format_ContainsAggregateSections_AndNoPerClickRows()
    {
        var rows = Enumerable.Range(0, 12).Select(i => Row($"ip{i}")).ToList();
        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(rows));
        var lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Equal("section,key,clicks,unique_clicks", lines[0]);
        Assert.Contains("totals,total,12,12", lines);
        Assert.Contains("totals,human,12,12", lines);
        Assert.Contains("source,Direct,12,", lines);
        Assert.Contains("day,2026-07-01,12,12", lines);
        Assert.Contains("hour_utc,09,12,", lines);

        // The whole export for 12 clicks is a handful of aggregate rows — nowhere near one per click,
        // and nothing resembling a hashed IP appears anywhere.
        Assert.DoesNotContain(lines, l => l.Contains("ip0"));
    }

    [Fact]
    public void Format_QuotesOperatorSuppliedLabelsContainingCommas()
    {
        // UTM values are operator text; a comma must not break the row shape.
        var rows = Enumerable.Range(0, 10).Select(i => Row($"ip{i}", utmCampaign: "spring, launch")).ToList();
        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(rows));

        Assert.Contains("utm_campaign,\"spring, launch\",10,", csv);
    }
}
