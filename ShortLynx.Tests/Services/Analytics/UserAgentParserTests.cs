using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class UserAgentParserTests
{
    private readonly UserAgentParser _sut = new();

    [Theory]
    [InlineData(null, DeviceType.Unknown)]
    [InlineData("", DeviceType.Unknown)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36", DeviceType.Desktop)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148", DeviceType.Mobile)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) Version/17 Safari/605.1.15", DeviceType.Tablet)]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)", DeviceType.Bot)]
    public void Parse_Device(string? ua, DeviceType expected)
        => Assert.Equal(expected, _sut.Parse(ua).Device);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36", "Chrome")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17 Safari/605.1.15", "Safari")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Gecko/20100101 Firefox/121.0", "Firefox")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120 Safari/537.36 Edg/120.0", "Edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120 Safari/537.36 OPR/106.0", "Opera")]
    public void Parse_Browser(string ua, string expected)
        => Assert.Equal(expected, _sut.Parse(ua).Browser);

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)", "macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)", "iOS")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Mobile", "Android")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64)", "Linux")]
    public void Parse_Os(string ua, string expected)
        => Assert.Equal(expected, _sut.Parse(ua).Os);

    [Fact]
    public void Parse_Bot_DoesNotDeriveBrowserOrOs()
    {
        var info = _sut.Parse("Googlebot/2.1 (+http://www.google.com/bot.html)");
        Assert.Equal(DeviceType.Bot, info.Device);
        Assert.Null(info.Browser);
        Assert.Null(info.Os);
    }
}
