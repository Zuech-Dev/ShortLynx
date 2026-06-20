namespace ShortLynx.Services.Redirect;

public class RedirectOptions
{
    public int CacheSlidingExpirationSeconds { get; set; } = 300;

    /// <summary>Max number of entries the redirect cache holds before evicting (each entry has Size 1).</summary>
    public long CacheSizeLimit { get; set; } = 100_000;

    /// <summary>How long a cache miss (unknown code) is remembered so a flood of random codes can't hammer the DB.</summary>
    public int CacheNegativeSeconds { get; set; } = 10;
}
