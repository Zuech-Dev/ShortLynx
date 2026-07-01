using Bunit;
using ShortLynx.Admin.Components;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Admin;

public class ClicksTableComponentTests : BunitContext
{
    private static ClickRow Click(DateTimeOffset at, ClickSource source, DeviceType device, string? referrer)
        => new(at, source, device, referrer);

    private static IReadOnlyList<ClickRow> Sample() =>
    [
        Click(new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero), ClickSource.Twitter, DeviceType.Mobile, "https://t.co/abc"),
        Click(new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero), ClickSource.Bluesky, DeviceType.Desktop, "https://bsky.app/x"),
        Click(new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero), ClickSource.Direct, DeviceType.Desktop, null),
    ];

    [Fact]
    public void Renders_AllRows_ByDefault()
    {
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, Sample()));
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Filter_ByPlatform_ReducesRows()
    {
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, Sample()));
        cut.Find("[data-testid=filter-source]").Change(nameof(ClickSource.Twitter));
        var rows = cut.FindAll("tbody tr");
        Assert.Single(rows);
        Assert.Contains("Twitter", rows[0].InnerHtml);
    }

    [Fact]
    public void Filter_ByReferrerContains_Matches()
    {
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, Sample()));
        cut.Find("[data-testid=filter-referrer]").Input("bsky");
        var rows = cut.FindAll("tbody tr");
        Assert.Single(rows);
        Assert.Contains("Bluesky", rows[0].InnerHtml);
    }

    [Fact]
    public void Filter_NoMatches_ShowsEmptyState()
    {
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, Sample()));
        cut.Find("[data-testid=filter-referrer]").Input("nomatch-xyz");
        Assert.NotNull(cut.Find("[data-testid=clicks-empty]"));
    }

    [Fact]
    public void Sort_ByPlatform_OrdersDescendingFirstClick()
    {
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, Sample()));
        cut.Find("[data-testid=sort-Source]").Click(); // first click on a new column ⇒ descending
        var rows = cut.FindAll("tbody tr");
        Assert.Contains("Twitter", rows[0].InnerHtml); // Twitter > Direct > Bluesky alphabetically
    }

    [Fact]
    public void Pagination_PagesThroughResults()
    {
        // 30 rows ⇒ two pages of 25 / 5.
        var rows = Enumerable.Range(0, 30)
            .Select(i => Click(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                ClickSource.Direct, DeviceType.Desktop, null))
            .ToList();
        var cut = Render<ClicksTable>(p => p.Add(c => c.Clicks, rows));

        Assert.Equal(25, cut.FindAll("tbody tr").Count);
        Assert.Contains("1–25 of 30", cut.Find("[data-testid=page-status]").TextContent);

        cut.Find("[data-testid=page-next]").Click();
        Assert.Equal(5, cut.FindAll("tbody tr").Count);
        Assert.Contains("26–30 of 30", cut.Find("[data-testid=page-status]").TextContent);
    }
}
