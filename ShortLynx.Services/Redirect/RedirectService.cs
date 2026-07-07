using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Campaigns;

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
            .Include(x => x.Link).ThenInclude(l => l.Campaign)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (sc is not null)
        {
            var entry = new RedirectCacheEntry(BuildDestination(sc.Link), sc.Id, null, null, sc.Link.CustomDomain?.Domain);
            cache.Set(key, entry, _cacheOpts);
            return EnforceHost(entry, host);
        }

        // Mode 2 — user-attributed code
        var ulc = await db.UserLinkCodeEntities
            .Include(x => x.Link).ThenInclude(l => l.CustomDomain)
            .Include(x => x.Link).ThenInclude(l => l.Campaign)
            .Where(x => x.Code == code && x.IsActive && x.Link.IsActive)
            .FirstOrDefaultAsync(ct);

        if (ulc is null)
        {
            // Unknown code — remember the miss briefly to absorb random-code floods.
            cache.Set(key, NegativeSentinel, _negativeCacheOpts);
            return null;
        }

        var pinnedHost = ulc.Link.CustomDomain?.Domain;

        if (!HostMatches(pinnedHost, host)) return null;

        // Already-redeemed one-time codes must not resolve. Claiming happens in TryClaimOneTimeAsync,
        // invoked by the handler after any disclosure choice — lookup alone never burns the code.
        if (ulc.IsOneTimeUse && ulc.IsUsed) return null;

        // Disclosure gate (TRACKING_DISCLOSURE_PLAN): required whenever the owning account has no
        // privacy policy URL configured. Account name is shown on the interstitial.
        var account = await db.AccountEntities
            .Where(a => a.Id == ulc.Link.AccountId)
            .Select(a => new { a.Name, a.PrivacyPolicyUrl })
            .FirstOrDefaultAsync(ct);

        var mode2Entry = new RedirectCacheEntry(
            BuildDestination(ulc.Link), null, ulc.Id, ulc.UserId, pinnedHost,
            DisclosureRequired: string.IsNullOrWhiteSpace(account?.PrivacyPolicyUrl),
            AccountId: ulc.Link.AccountId,
            OperatorName: account?.Name,
            IsOneTimeUse: ulc.IsOneTimeUse);

        // Don't cache one-time-use codes — the IsUsed flag changes after first use.
        if (!ulc.IsOneTimeUse)
            cache.Set(key, mode2Entry, _cacheOpts);

        return mode2Entry;
    }

    public async Task<bool> TryClaimOneTimeAsync(Guid userLinkCodeId, CancellationToken ct = default)
    {
        // Atomic claim: only the request that flips IsUsed from false wins, so two concurrent hits
        // can't both redirect. No DateTimeOffset in the predicate (SQLite-safe).
        var claimed = await db.UserLinkCodeEntities
            .Where(x => x.Id == userLinkCodeId && !x.IsUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsUsed, true), ct);
        return claimed > 0;
    }

    // Resolves the click-through target: the link's destination with the campaign's UTM template merged
    // in (if any). Computed once here and cached, so the hot redirect path doesn't re-merge per request.
    private static string BuildDestination(LinkEntity link)
        => link.Campaign is { } c
            ? UtmTemplate.Apply(link.OriginalUrl, c.UtmSource, c.UtmMedium, c.UtmCampaign)
            : link.OriginalUrl;

    // Pinned links resolve only under their host; unpinned links (null PinnedHost) resolve anywhere.
    private static RedirectCacheEntry? EnforceHost(RedirectCacheEntry entry, string? host)
        => HostMatches(entry.PinnedHost, host) ? entry : null;

    private static bool HostMatches(string? pinnedHost, string? requestHost)
        => pinnedHost is null || string.Equals(pinnedHost, requestHost, StringComparison.OrdinalIgnoreCase);
}
