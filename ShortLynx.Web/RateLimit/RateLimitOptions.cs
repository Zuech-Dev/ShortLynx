namespace ShortLynx.Web.RateLimit;

public class RateLimitOptions
{
    public int PermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}
