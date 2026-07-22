namespace ShortLynx.Core.RateLimit;

/// <summary>Bound from the "RateLimit" config section. Per-IP fixed-window limits on sensitive endpoints.</summary>
public sealed class RateLimitOptions
{
    public int MagicLinkPermitLimit { get; set; } = 5;
    public int MagicLinkWindowSeconds { get; set; } = 300;

    public int ApiKeyPermitLimit { get; set; } = 10;
    public int ApiKeyWindowSeconds { get; set; } = 300;

    // Generous: a well-behaved client refreshes ~once per access-token lifetime (15 min), so even a
    // large NAT'd office fits — but token stuffing at scale does not.
    public int RefreshPermitLimit { get; set; } = 60;
    public int RefreshWindowSeconds { get; set; } = 300;

    // Debounced availability checks are a handful per code chosen; this permits fast interactive typing
    // while making enumeration of taken codes expensive.
    public int CustomCodeCheckPermitLimit { get; set; } = 40;
    public int CustomCodeCheckWindowSeconds { get; set; } = 60;
}
