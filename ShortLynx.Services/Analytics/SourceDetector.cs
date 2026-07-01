using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// Maps a request's Referer to a coarse platform bucket (<see cref="ClickSource"/>) at visit-write time.
/// Pure and deterministic so it can be unit-tested over a table of representative strings and run inline
/// on the batched write path with no I/O. Device classification lives in <see cref="IUserAgentParser"/>.
/// </summary>
public static class SourceDetector
{
    /// <summary>Classifies the platform a click came from by the referrer's host. Empty ⇒ Direct.</summary>
    public static ClickSource DetectSource(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)) return ClickSource.Direct;

        var host = ExtractHost(referrer);
        if (host.Length == 0) return ClickSource.Other;

        return host switch
        {
            _ when HostMatches(host, "t.co", "twitter.com", "x.com") => ClickSource.Twitter,
            _ when host.Contains("bsky.") || HostMatches(host, "bsky.app", "bsky.social") => ClickSource.Bluesky,
            _ when HostMatches(host, "linkedin.com", "lnkd.in") => ClickSource.LinkedIn,
            _ when HostMatches(host, "reddit.com", "redd.it") => ClickSource.Reddit,
            _ when HostMatches(host, "threads.net", "threads.com") => ClickSource.Threads,
            _ when HostMatches(host, "instagram.com") => ClickSource.Instagram,
            _ when HostMatches(host, "facebook.com", "fb.me", "fb.com") => ClickSource.Facebook,
            _ when IsMastodon(host) => ClickSource.Mastodon,
            _ => ClickSource.Other,
        };
    }

    // Matches the host exactly or as a subdomain (e.g. "l.facebook.com" matches "facebook.com"), so a
    // share wrapper on a known platform still attributes correctly without matching unrelated suffixes.
    private static bool HostMatches(string host, params string[] domains)
    {
        foreach (var d in domains)
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal))
                return true;
        return false;
    }

    // Mastodon is federated across thousands of instances; there's no single host. Best-effort: the
    // big public instances plus the "mastodon" naming convention many instances follow.
    private static bool IsMastodon(string host)
        => host.Contains("mastodon")
        || HostMatches(host, "mas.to", "mstdn.social", "fosstodon.org", "hachyderm.io",
                             "infosec.exchange", "techhub.social", "social.vivaldi.net");

    // Pulls the lowercase host out of a referrer. Referrers are absolute URLs, but be defensive: some
    // clients send bare hosts or app URIs (android-app://…), which Uri parses with an empty Host.
    private static string ExtractHost(string referrer)
    {
        var value = referrer.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            return uri.Host.ToLowerInvariant();

        // Fall back to treating the leading token as a host (strip any path/query).
        var slash = value.IndexOf('/');
        var bare = (slash >= 0 ? value[..slash] : value).ToLowerInvariant();
        return bare.Contains('.') ? bare : string.Empty;
    }
}
