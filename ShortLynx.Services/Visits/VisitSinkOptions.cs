namespace ShortLynx.Services.Visits;

public class VisitSinkOptions
{
    public int ChannelCapacity { get; set; } = 10_000;
    public int BatchSize { get; set; } = 100;
    public int DrainIntervalMs { get; set; } = 500;

    /// <summary>
    /// Secret key for HMAC IP hashing. Without a secret pepper the ~4 billion IPv4 addresses are
    /// trivially brute-forceable, making the stored hash reversible PII. Set per-environment via
    /// user-secrets / env (VisitSink:IpHashPepper). Empty default for dev/tests.
    /// </summary>
    public string IpHashPepper { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to a MaxMind GeoLite2-City database (.mmdb). When set and the file exists,
    /// visits get Country + IANA TimeZone dimensions (and nothing finer -- see IGeoIpResolver);
    /// when empty, geo resolution is disabled entirely. Free download, MaxMind account required.
    /// </summary>
    public string? GeoIpDatabasePath { get; set; }

    /// <summary>
    /// Delete visit rows older than this many days (nightly prune). Null -- the default -- keeps
    /// analytics forever; self-hosters set this directly, the hosted tier drives it per plan.
    /// </summary>
    public int? AnalyticsRetentionDays { get; set; }
}
