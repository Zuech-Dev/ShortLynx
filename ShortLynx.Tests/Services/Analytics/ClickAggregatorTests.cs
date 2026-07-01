using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class ClickAggregatorTests
{
    private static VisitRow Row(string ip, string? browser = null, string? os = null,
        string? country = null, string? language = null)
        => new(ip, ClickSource.Direct, DeviceType.Desktop, DateTimeOffset.UtcNow, browser, os, country, language);

    [Fact]
    public void Summarize_TalliesStringDimensions_DroppingNulls()
    {
        var rows = new[]
        {
            Row("ip1", browser: "Chrome", language: "en"),
            Row("ip2", browser: "Chrome", language: "en"),
            Row("ip3", browser: "Safari", language: null),
            Row("ip4", browser: null,     language: "fr"),
        };

        var b = ClickAggregator.Summarize(rows);

        // Browsers: Chrome (2) before Safari (1); the null-browser row is dropped (sum 3, not 4).
        Assert.Equal(2, b.Browsers.Count);
        Assert.Equal("Chrome", b.Browsers[0].Label);
        Assert.Equal(2, b.Browsers[0].Count);
        Assert.Equal(3, b.Browsers.Sum(x => x.Count));

        Assert.Equal(2, b.Languages.Single(l => l.Label == "en").Count);

        // Country was never set on any row → empty (so the UI hides the section until GeoIP lands).
        Assert.Empty(b.Countries);
        Assert.Empty(b.OperatingSystems);
    }
}
