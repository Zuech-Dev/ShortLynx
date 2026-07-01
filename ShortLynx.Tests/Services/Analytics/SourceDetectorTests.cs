using ShortLynx.Data.Enums;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class SourceDetectorTests
{
    [Theory]
    [InlineData(null, ClickSource.Direct)]
    [InlineData("", ClickSource.Direct)]
    [InlineData("   ", ClickSource.Direct)]
    // Twitter / X
    [InlineData("https://t.co/abc123", ClickSource.Twitter)]
    [InlineData("https://twitter.com/someone/status/1", ClickSource.Twitter)]
    [InlineData("https://x.com/someone", ClickSource.Twitter)]
    // Bluesky
    [InlineData("https://bsky.app/profile/x", ClickSource.Bluesky)]
    [InlineData("https://staging.bsky.app/", ClickSource.Bluesky)]
    [InlineData("https://bsky.social/", ClickSource.Bluesky)]
    // LinkedIn
    [InlineData("https://www.linkedin.com/feed/", ClickSource.LinkedIn)]
    [InlineData("https://lnkd.in/abcd", ClickSource.LinkedIn)]
    // Reddit
    [InlineData("https://www.reddit.com/r/dotnet/", ClickSource.Reddit)]
    [InlineData("https://out.reddit.com/x", ClickSource.Reddit)]
    [InlineData("https://redd.it/xyz", ClickSource.Reddit)]
    // Facebook
    [InlineData("https://l.facebook.com/l.php?u=x", ClickSource.Facebook)]
    [InlineData("https://m.facebook.com/", ClickSource.Facebook)]
    [InlineData("https://fb.me/x", ClickSource.Facebook)]
    // Instagram
    [InlineData("https://l.instagram.com/?u=x", ClickSource.Instagram)]
    [InlineData("https://www.instagram.com/", ClickSource.Instagram)]
    // Threads
    [InlineData("https://www.threads.net/@someone", ClickSource.Threads)]
    // Mastodon (federated)
    [InlineData("https://mastodon.social/@someone", ClickSource.Mastodon)]
    [InlineData("https://mas.to/@someone", ClickSource.Mastodon)]
    [InlineData("https://hachyderm.io/@someone", ClickSource.Mastodon)]
    // Unknown referrer host
    [InlineData("https://news.ycombinator.com/item?id=1", ClickSource.Other)]
    [InlineData("https://example.com/blog", ClickSource.Other)]
    public void DetectSource_MapsReferrerToPlatform(string? referrer, ClickSource expected)
        => Assert.Equal(expected, SourceDetector.DetectSource(referrer));

    [Fact]
    public void DetectSource_IsCaseInsensitiveOnHost()
        => Assert.Equal(ClickSource.Twitter, SourceDetector.DetectSource("HTTPS://T.CO/ABC"));

    [Fact]
    public void DetectSource_UnrecognizedAppReferrer_IsOther()
        // In-app referrers (android-app://<package>) are a real referrer but Phase 0 only decodes web
        // hosts, so an unmapped package is "Other", not "Direct". App-package decoding is a follow-up.
        => Assert.Equal(ClickSource.Other, SourceDetector.DetectSource("android-app://com.google.android.gm"));

    // Device classification moved to IUserAgentParser (see UserAgentParserTests).
}
