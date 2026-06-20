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
    private readonly RedirectOptions _options = options.Value;

    // Sentinel cached for unknown codes so a flood of random codes can't hit the DB on every request.
    // Compared by reference; never returned to callers.
    private static readonly RedirectCacheEntry NegativeSentinel = new(string.Empty, null, null, null);

    private readonly MemoryCacheEntryOptions _cacheOpts = new MemoryCacheEntryOptions()
        .SetSlidingExpiration(TimeSpan.FromSeconds(options.Value.CacheSlidingExpirationSeconds))
        .SetSize(1);

    private readonly MemoryCacheEntryOptions _negativeCacheOpts = new MemoryCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromSeconds(options.Value.CacheNegativeSeconds))
        .SetSize(1);

    public async Task<RedirectCacheEntry?> LookupAsync(string code, CancellationToken ct = default)
    {
        var key = $"redirect:{code}";
        if (cache.TryGetValue(key, out RedirectCacheEntry? hit))
            return ReferenceEquals(hit, NegativeSentinel) ? null : hit;

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

        if (ulc is null)
        {
            // Unknown code — remember the miss briefly to absorb random-code floods.
            cache.Set(key, NegativeSentinel, _negativeCacheOpts);
            return null;
        }

        // One-time-use codes that have been redeemed must not redirect again.
        if (ulc.IsOneTimeUse)
        {
            if (ulc.IsUsed) return null;

            // Atomically claim the code: only the request that flips IsUsed from false wins, so two
            // concurrent hits can't both redirect. No DateTimeOffset in the predicate (SQLite-safe).
            var claimed = await db.UserLinkCodeEntities
                .Where(x => x.Id == ulc.Id && !x.IsUsed)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsUsed, true), ct);

            if (claimed == 0) return null;
        }

        var mode2Entry = new RedirectCacheEntry(ulc.Link.OriginalUrl, null, ulc.Id, ulc.UserId);

        // Don't cache one-time-use codes — the IsUsed flag changes after first use.
        if (!ulc.IsOneTimeUse)
            cache.Set(key, mode2Entry, _cacheOpts);

        return mode2Entry;
    }
}
