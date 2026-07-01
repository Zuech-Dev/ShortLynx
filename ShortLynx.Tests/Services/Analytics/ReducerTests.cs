using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class ReferrerReducerTests
{
    private readonly ReferrerReducer _sut = new();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not a url", null)]
    [InlineData("https://t.co/abc123", "t.co")]
    [InlineData("https://www.linkedin.com/feed/?ref=abc", "linkedin.com")]       // path + query dropped
    [InlineData("https://news.ycombinator.com/item?id=1&secret=x", "news.ycombinator.com")]
    [InlineData("HTTPS://WWW.Example.COM/Path", "example.com")]                   // lowercased, www stripped
    public void Host_ReducesToHost(string? referrer, string? expected)
        => Assert.Equal(expected, _sut.Host(referrer));
}

public class LanguageReducerTests
{
    private readonly LanguageReducer _sut = new();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("*", null)]
    [InlineData("en-US,en;q=0.9,fr;q=0.8", "en")]
    [InlineData("fr-CA", "fr")]
    [InlineData("de;q=0.7", "de")]
    [InlineData("123", null)]
    public void Primary_ReducesToPrimarySubtag(string? header, string? expected)
        => Assert.Equal(expected, _sut.Primary(header));
}
