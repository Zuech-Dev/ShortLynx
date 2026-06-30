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

    [Theory]
    [InlineData(null, DeviceType.Unknown)]
    [InlineData("", DeviceType.Unknown)]
    // Desktop
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36", DeviceType.Desktop)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17 Safari/605.1.15", DeviceType.Desktop)]
    // Mobile
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148", DeviceType.Mobile)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Mobile Safari/537.36", DeviceType.Mobile)]
    // Tablet
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17 Safari/605.1.15", DeviceType.Tablet)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X910) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36", DeviceType.Tablet)]
    // Bot / preview fetchers
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", DeviceType.Bot)]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", DeviceType.Bot)]
    [InlineData("Twitterbot/1.0", DeviceType.Bot)]
    [InlineData("curl/8.4.0", DeviceType.Bot)]
    public void DetectDevice_MapsUserAgentToDeviceClass(string? userAgent, DeviceType expected)
        => Assert.Equal(expected, SourceDetector.DetectDevice(userAgent));
}
