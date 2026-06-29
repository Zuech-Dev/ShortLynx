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

    public async Task<RedirectCacheEntry?> LookupAsync(string code, string? host = null, CancellationToken ct = default)
    {
        var key = $"redirect:{code}";
        if (cache.TryGetValue(key, out RedirectCacheEntry? hit))
            return ReferenceEquals(hit, NegativeSentinel) ? null : EnforceHost(hit!, host);

        // Mode 1 — anonymous short code
        var sc = await db.ShortCodeEntities
            .Include(x => x.Link).ThenInclude(l => l.CustomDomain)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (sc is not null)
        {
            var entry = new RedirectCacheEntry(sc.Link.OriginalUrl, sc.Id, null, null, sc.Link.CustomDomain?.Domain);
            cache.Set(key, entry, _cacheOpts);
            return EnforceHost(entry, host);
        }

        // Mode 2 — user-attributed code
        var ulc = await db.UserLinkCodeEntities
            .Include(x => x.Link).ThenInclude(l => l.CustomDomain)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (ulc is null)
        {
            // Unknown code — remember the miss briefly to absorb random-code floods.
            cache.Set(key, NegativeSentinel, _negativeCacheOpts);
            return null;
        }

        var pinnedHost = ulc.Link.CustomDomain?.Domain;

        // Reject the wrong host *before* claiming a one-time code, so a mismatched request can't burn it.
        if (!HostMatches(pinnedHost, host)) return null;

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

        var mode2Entry = new RedirectCacheEntry(ulc.Link.OriginalUrl, null, ulc.Id, ulc.UserId, pinnedHost);

        // Don't cache one-time-use codes — the IsUsed flag changes after first use.
        if (!ulc.IsOneTimeUse)
            cache.Set(key, mode2Entry, _cacheOpts);

        return mode2Entry;
    }

    // Pinned links resolve only under their host; unpinned links (null PinnedHost) resolve anywhere.
    private static RedirectCacheEntry? EnforceHost(RedirectCacheEntry entry, string? host)
        => HostMatches(entry.PinnedHost, host) ? entry : null;

    private static bool HostMatches(string? pinnedHost, string? requestHost)
        => pinnedHost is null || string.Equals(pinnedHost, requestHost, StringComparison.OrdinalIgnoreCase);
}
