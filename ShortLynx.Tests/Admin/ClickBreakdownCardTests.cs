using Bunit;
using ShortLynx.Admin.Components;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Admin;

public class ClickBreakdownCardTests : BunitContext
{
    private static VisitRow Row(string ip, DeviceType device)
        => new(ip, ClickSource.Direct, device, DateTimeOffset.UtcNow, null, null, null, null, null);

    [Fact]
    public void ShowsSuspectedScanners_AsPartOfFilteredBots()
    {
        var b = ClickAggregator.Summarize(
        [
            Row("h1", DeviceType.Mobile),
            Row("s1", DeviceType.SuspectedAutomated),
            Row("s2", DeviceType.SuspectedAutomated),
            Row("b1", DeviceType.Bot),
        ]);

        var cut = Render<ClickBreakdownCard>(p => p.Add(c => c.Breakdown, b));

        Assert.Equal("1", cut.Find("[data-testid=total-clicks]").TextContent);
        Assert.Contains("+3 bots filtered", cut.Find("[data-testid=bot-clicks]").TextContent);
        Assert.Contains("incl. 2 suspected link scanners", cut.Find("[data-testid=suspected-automated-clicks]").TextContent);
    }

    [Fact]
    public void HidesScannerLine_WhenThereAreNone()
    {
        var b = ClickAggregator.Summarize([Row("h1", DeviceType.Desktop), Row("b1", DeviceType.Bot)]);

        var cut = Render<ClickBreakdownCard>(p => p.Add(c => c.Breakdown, b));

        Assert.Empty(cut.FindAll("[data-testid=suspected-automated-clicks]"));
    }
}
