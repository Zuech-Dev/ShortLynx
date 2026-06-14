namespace ShortLynx.Core.RateLimit;

/// <summary>Bound from the "RateLimit" config section. Per-IP fixed-window limits on sensitive endpoints.</summary>
public sealed class RateLimitOptions
{
    public int MagicLinkPermitLimit { get; set; } = 5;
    public int MagicLinkWindowSeconds { get; set; } = 300;

    public int ApiKeyPermitLimit { get; set; } = 10;
    public int ApiKeyWindowSeconds { get; set; } = 300;
}
