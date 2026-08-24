namespace ShortLynx.Services.Redirect;

public class RedirectOptions
{
    public int CacheSlidingExpirationSeconds { get; set; } = 300;

    /// <summary>Max number of entries the redirect cache holds before evicting (each entry has Size 1).</summary>
    public long CacheSizeLimit { get; set; } = 100_000;

    /// <summary>How long a cache miss (unknown code) is remembered so a flood of random codes can't hammer the DB.</summary>
    public int CacheNegativeSeconds { get; set; } = 10;

    /// <summary>
    /// Where to send a browser that requests a code this deployment has never issued (typo, expired
    /// one-time code, or someone probing for valid codes). Null/empty (the default) means exactly
    /// today's behavior: a plain 404. Deliberately NOT defaulted to anything -- this is the OSS redirect
    /// app every self-hoster runs, so a hardcoded fallback here would silently send someone else's
    /// visitors to a URL this project chose, not one they did. Set per-deployment (e.g. the hosted
    /// product points its own shrtlynx.com at shortlynx.dev); leave unset otherwise.
    /// </summary>
    public string? NotFoundRedirectUrl { get; set; }

    /// <summary>
    /// Where to send a visitor hitting the homepage to learn about or buy the product, as opposed to
    /// a redirect-service visitor following a short link. Null/empty (the default) renders this
    /// deployment's own Index page — the OSS self-host pitch, unaffected. Set per-deployment (the
    /// hosted product points its own shrtlynx.com at shortlynx.dev, its actual marketing surface) --
    /// leave unset for a normal self-hosted instance, which has no separate marketing site to point at.
    /// </summary>
    public string? MarketingRedirectUrl { get; set; }
}
