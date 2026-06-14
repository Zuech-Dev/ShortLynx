using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.Redirect;

public sealed class RedirectService(
    ShortLynxDbContext db,
    IMemoryCache cache,
    IOptions<RedirectOptions> options) : IRedirectService
{
    private readonly MemoryCacheEntryOptions _cacheOpts = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromSeconds(options.Value.CacheSlidingExpirationSeconds));

    public async Task<RedirectCacheEntry?> LookupAsync(string code, CancellationToken ct = default)
    {
        var key = $"redirect:{code}";
        if (cache.TryGetValue(key, out RedirectCacheEntry? hit))
            return hit;

        // Mode 1 — anonymous short code
        var sc = await db.ShortCodeEntities
            .Include(x => x.Link)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (sc is not null)
        {
            var entry = new RedirectCacheEntry(sc.Link.OriginalUrl, sc.Id, null, null);
            cache.Set(key, entry, _cacheOpts);
            return entry;
        }

        // Mode 2 — user-attributed code
        var ulc = await db.UserLinkCodeEntities
            .Include(x => x.Link)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (ulc is null) return null;

        // One-time-use codes that have been redeemed must not redirect again.
        if (ulc.IsOneTimeUse && ulc.IsUsed) return null;

        var mode2Entry = new RedirectCacheEntry(ulc.Link.OriginalUrl, null, ulc.Id, ulc.UserId);

        // Don't cache one-time-use codes — the IsUsed flag changes after first use.
        if (!ulc.IsOneTimeUse)
            cache.Set(key, mode2Entry, _cacheOpts);

        return mode2Entry;
    }
}
