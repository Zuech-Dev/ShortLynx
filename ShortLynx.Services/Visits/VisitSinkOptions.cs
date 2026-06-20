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
}
