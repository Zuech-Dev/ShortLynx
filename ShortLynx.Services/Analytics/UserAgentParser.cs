using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Analytics;

/// <summary>Coarse, low-entropy buckets derived from a User-Agent — never the raw string.</summary>
public sealed record UserAgentInfo(string? Browser, string? Os, DeviceType Device);

/// <summary>
/// Parses a User-Agent into browser/OS/device buckets so the raw string need not be persisted (the raw
/// UA is a fingerprint vector). Pure and dependency-free; the interface lets a richer parser (e.g. UAParser)
/// be swapped in later without touching the write path.
/// </summary>
public interface IUserAgentParser
{
    UserAgentInfo Parse(string? userAgent);
}

public sealed class UserAgentParser : IUserAgentParser
{
    public UserAgentInfo Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return new UserAgentInfo(null, null, DeviceType.Unknown);

        var ua = userAgent.ToLowerInvariant();
        var device = DetectDevice(ua);

        // Don't derive browser/OS for automated clients — the bucket that matters is "Bot".
        return device == DeviceType.Bot
            ? new UserAgentInfo(null, null, DeviceType.Bot)
            : new UserAgentInfo(DetectBrowser(ua), DetectOs(ua), device);
    }

    private static DeviceType DetectDevice(string ua)
    {
        if (IsBot(ua)) return DeviceType.Bot;
        if (ua.Contains("ipad") || (ua.Contains("android") && !ua.Contains("mobile")) || ua.Contains("tablet"))
            return DeviceType.Tablet;
        if (ua.Contains("mobile") || ua.Contains("iphone") || ua.Contains("ipod") || ua.Contains("android"))
            return DeviceType.Mobile;
        return DeviceType.Desktop;
    }

    // Order matters: Edge/Opera UAs also contain "chrome"; Chrome UAs also contain "safari".
    private static string? DetectBrowser(string ua)
    {
        if (ua.Contains("edg/") || ua.Contains("edga/") || ua.Contains("edgios/")) return "Edge";
        if (ua.Contains("opr/") || ua.Contains("opera")) return "Opera";
        if (ua.Contains("firefox") || ua.Contains("fxios")) return "Firefox";
        if (ua.Contains("chrome") || ua.Contains("crios")) return "Chrome";
        if (ua.Contains("safari")) return "Safari";
        return null;
    }

    // Order matters: Android UAs contain "linux"; iOS UAs contain "mac os x".
    private static string? DetectOs(string ua)
    {
        if (ua.Contains("windows")) return "Windows";
        if (ua.Contains("android")) return "Android";
        if (ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ipod")) return "iOS";
        if (ua.Contains("mac os x") || ua.Contains("macintosh")) return "macOS";
        if (ua.Contains("linux")) return "Linux";
        return null;
    }

    private static bool IsBot(string ua)
        => ua.Contains("bot") || ua.Contains("crawler") || ua.Contains("spider") || ua.Contains("slurp")
        || ua.Contains("facebookexternalhit") || ua.Contains("embedly") || ua.Contains("preview")
        || ua.Contains("curl") || ua.Contains("wget") || ua.Contains("python-requests")
        || ua.Contains("headless") || ua.Contains("scan");
}
