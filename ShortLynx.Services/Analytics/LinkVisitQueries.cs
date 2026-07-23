using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Analytics;

/// <summary>A visit tagged with the link it belongs to, for callers that query several links at once.</summary>
public readonly record struct LinkVisitRow(Guid LinkId, VisitRow Row);

/// <summary>Clicks on one code of a link (the shared code, or one recipient's code).</summary>
public readonly record struct CodeClickCount(Guid CodeId, string Code, Guid? UserId, long Clicks);

/// <summary>
/// How a link's clicks split by what we can actually attribute them to. Post clicks are *exact* (the
/// code identifies the post); organic clicks are everything that arrived on the link's shared code —
/// QR scans, the copy button, reshares — where a referrer guess (<c>ClickSource</c>) is the only signal
/// available. Post clicks are a SUBSET of the link's total, never a sibling to be added to it.
/// </summary>
public readonly record struct LinkAttributionSplit(
    long AttributedClicks,
    long OrganicClicks,
    IReadOnlyList<PostClickCount> Posts);

/// <summary>Clicks attributed to one published post, with the engagement the platform reported.</summary>
public readonly record struct PostClickCount(
    Guid SocialPostId,
    string Platform,
    string Handle,
    string? PostUrl,
    DateTimeOffset PostedAt,
    long Clicks,
    long UniqueClicks,
    long? Impressions,
    long? Likes);

/// <summary>
/// **The** definition of "a click on this link" — every analytics surface reads through here so the rule
/// lives in one place. A link's clicks come from more than one code table:
///
/// <list type="bullet">
/// <item>the link's single shared code (<see cref="ShortCodeEntity"/>) → <c>Visits</c> — the URL people
///       get from a QR, the copy button, or a reshare</item>
/// <item>each recipient's code (<see cref="UserLinkCodeEntity"/>) → <c>UserVisits</c> — Mode 2, so a
///       click is attributable to one person</item>
/// </list>
///
/// That rule used to be hand-rolled at ~7 call sites, each reproducing the same joins and the same
/// 13-field projection. Adding a code source meant finding all of them; missing one silently
/// undercounted. Keep new sources here.
/// </summary>
public static class LinkVisitQueries
{
    // One place that knows how to turn a Visits/UserVisits row into a VisitRow. Both tables carry the
    // same dimensions (they're near-identical by design), so the projection is shared.
    private static VisitRow ToRow(
        string hashedIp, Data.Enums.ClickSource source, Data.Enums.DeviceType device, DateTimeOffset clickedAt,
        string? browser, string? os, string? country, string? language, string? navigationType,
        string? timeZone, string? utmSource, string? utmMedium, string? utmCampaign, string? referrerHost)
        => new(hashedIp, source, device, clickedAt, browser, os, country, language,
               navigationType, timeZone, utmSource, utmMedium, utmCampaign, referrerHost);

    /// <summary>Every click on the given links, each tagged with its link. The core query; others build on it.</summary>
    public static async Task<List<LinkVisitRow>> LoadRowsByLinkAsync(
        ShortLynxDbContext db, IReadOnlyCollection<Guid> linkIds, CancellationToken ct = default)
    {
        var tagged = new List<LinkVisitRow>();
        if (linkIds.Count == 0) return tagged;

        // Shared codes → Visits.
        var shortCodeToLink = await db.ShortCodeEntities
            .Where(sc => linkIds.Contains(sc.LinkId))
            .Select(sc => new { sc.Id, sc.LinkId })
            .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        if (shortCodeToLink.Count > 0)
        {
            var scIds = shortCodeToLink.Keys.ToList();
            var visits = await db.VisitEntities
                .Where(v => v.ShortCodeId != null && scIds.Contains(v.ShortCodeId.Value))
                .Select(v => new
                {
                    v.ShortCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os,
                    v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                    v.UtmCampaign, v.ReferrerHost,
                })
                .ToListAsync(ct);

            tagged.AddRange(visits.Select(v => new LinkVisitRow(
                shortCodeToLink[v.ShortCodeId!.Value],
                ToRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country,
                      v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                      v.UtmCampaign, v.ReferrerHost))));
        }

        // Per-post codes → Visits. Each published social post gets its own code (SocialPostCodeEntity),
        // so its clicks land in the same table as shared-code clicks but attribute exactly to the post
        // rather than being guessed from a referrer.
        var postCodeToLink = await db.SocialPostCodeEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new { c.Id, c.LinkId })
            .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        if (postCodeToLink.Count > 0)
        {
            var postCodeIds = postCodeToLink.Keys.ToList();
            var postVisits = await db.VisitEntities
                .Where(v => v.SocialPostCodeId != null && postCodeIds.Contains(v.SocialPostCodeId.Value))
                .Select(v => new
                {
                    v.SocialPostCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os,
                    v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                    v.UtmCampaign, v.ReferrerHost,
                })
                .ToListAsync(ct);

            tagged.AddRange(postVisits.Select(v => new LinkVisitRow(
                postCodeToLink[v.SocialPostCodeId!.Value],
                ToRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country,
                      v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                      v.UtmCampaign, v.ReferrerHost))));
        }

        // Recipient codes → UserVisits.
        var userCodeToLink = await db.UserLinkCodeEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new { c.Id, c.LinkId })
            .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        if (userCodeToLink.Count > 0)
        {
            var codeIds = userCodeToLink.Keys.ToList();
            var userVisits = await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new
                {
                    v.UserLinkCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os,
                    v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                    v.UtmCampaign, v.ReferrerHost,
                })
                .ToListAsync(ct);

            tagged.AddRange(userVisits.Select(v => new LinkVisitRow(
                userCodeToLink[v.UserLinkCodeId],
                ToRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country,
                      v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                      v.UtmCampaign, v.ReferrerHost))));
        }

        return tagged;
    }

    /// <summary>Every click on one link.</summary>
    public static async Task<List<VisitRow>> LoadLinkRowsAsync(
        ShortLynxDbContext db, LinkEntity link, CancellationToken ct = default)
        => (await LoadRowsByLinkAsync(db, [link.Id], ct)).Select(t => t.Row).ToList();

    /// <summary>Every click across a campaign's links, tagged by link (for the per-link table).</summary>
    public static async Task<List<LinkVisitRow>> LoadCampaignRowsByLinkAsync(
        ShortLynxDbContext db, Guid campaignId, Guid accountId, CancellationToken ct = default)
    {
        var linkIds = await db.LinkEntities
            .Where(l => l.CampaignId == campaignId && l.AccountId == accountId)
            .Select(l => l.Id)
            .ToListAsync(ct);
        return await LoadRowsByLinkAsync(db, linkIds, ct);
    }

    /// <summary>Every click across a campaign's links, flattened.</summary>
    public static async Task<List<VisitRow>> LoadCampaignRowsAsync(
        ShortLynxDbContext db, Guid campaignId, Guid accountId, CancellationToken ct = default)
        => (await LoadCampaignRowsByLinkAsync(db, campaignId, accountId, ct)).Select(t => t.Row).ToList();

    /// <summary>
    /// Per-code click counts for one link — the shared code for anonymous links, or every recipient's
    /// code for Mode 2. Codes with no clicks are included (a zero-click recipient is a real answer).
    /// </summary>
    public static async Task<List<CodeClickCount>> LoadCodeCountsAsync(
        ShortLynxDbContext db, LinkEntity link, CancellationToken ct = default)
    {
        if (link.Mode == Data.Enums.LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities
                .Where(s => s.LinkId == link.Id)
                .Select(s => new { s.Id, s.Code })
                .FirstOrDefaultAsync(ct);
            if (sc is null) return [];

            // Anonymous links carry the shared code plus a code per published post; the "code count"
            // shown for the link is all of them summed (per-post breakdown lives in the posts view).
            var sharedClicks = await db.VisitEntities.LongCountAsync(v => v.ShortCodeId == sc.Id, ct);
            var postClicks = await db.VisitEntities.LongCountAsync(
                v => v.SocialPostCode!.LinkId == link.Id, ct);
            return [new CodeClickCount(sc.Id, sc.Code, null, sharedClicks + postClicks)];
        }

        var codes = await db.UserLinkCodeEntities
            .Where(c => c.LinkId == link.Id)
            .Select(c => new { c.Id, c.Code, c.UserId })
            .ToListAsync(ct);
        if (codes.Count == 0) return [];

        var codeIds = codes.Select(c => c.Id).ToList();
        var countByCode = (await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .GroupBy(v => v.UserLinkCodeId)
                .Select(g => new { CodeId = g.Key, Clicks = g.LongCount() })
                .ToListAsync(ct))
            .ToDictionary(x => x.CodeId, x => x.Clicks);

        return codes
            .Select(c => new CodeClickCount(c.Id, c.Code, c.UserId, countByCode.GetValueOrDefault(c.Id, 0)))
            .ToList();
    }

    /// <summary>Total clicks per link — for list views that need a number, not the rows.</summary>
    public static async Task<Dictionary<Guid, long>> CountByLinkAsync(
        ShortLynxDbContext db, IReadOnlyCollection<Guid> linkIds, CancellationToken ct = default)
    {
        var counts = new Dictionary<Guid, long>();
        if (linkIds.Count == 0) return counts;

        // Counted server-side rather than by loading rows — list views can span every link in an account.
        var shared = await db.VisitEntities
            .Where(v => v.ShortCodeId != null && linkIds.Contains(v.ShortCode!.LinkId))
            .GroupBy(v => v.ShortCode!.LinkId)
            .Select(g => new { LinkId = g.Key, Clicks = g.LongCount() })
            .ToListAsync(ct);
        foreach (var s in shared) counts[s.LinkId] = s.Clicks;

        var post = await db.VisitEntities
            .Where(v => v.SocialPostCodeId != null && linkIds.Contains(v.SocialPostCode!.LinkId))
            .GroupBy(v => v.SocialPostCode!.LinkId)
            .Select(g => new { LinkId = g.Key, Clicks = g.LongCount() })
            .ToListAsync(ct);
        foreach (var p in post)
            counts[p.LinkId] = counts.GetValueOrDefault(p.LinkId) + p.Clicks;

        var recipient = await db.UserVisitEntities
            .Where(v => linkIds.Contains(v.UserLinkCode.LinkId))
            .GroupBy(v => v.UserLinkCode.LinkId)
            .Select(g => new { LinkId = g.Key, Clicks = g.LongCount() })
            .ToListAsync(ct);
        foreach (var r in recipient)
            counts[r.LinkId] = counts.GetValueOrDefault(r.LinkId) + r.Clicks;

        return counts;
    }

    /// <summary>
    /// Splits a link's clicks into what each published post drove versus everything else ("organic" —
    /// the shared code: QR, copy button, reshares). This is the answer referrer sniffing could never
    /// give: two posts on the same platform are separable, and it works under DNT/GPC because the code
    /// identifies the post, not the clicker.
    /// </summary>
    public static async Task<LinkAttributionSplit> LoadAttributionSplitAsync(
        ShortLynxDbContext db, Guid linkId, CancellationToken ct = default)
    {
        var posts = await db.SocialPostEntities
            .Where(p => p.LinkId == linkId)
            .Select(p => new
            {
                p.Id, p.Platform, p.Handle, p.PostUrl, p.PostedAt, p.Impressions, p.Likes,
            })
            .ToListAsync(ct);

        // Clicks per post code, plus the hashed IPs so uniques can be counted per post. Pulled as rows
        // (not GroupBy in SQL) because unique-counting needs the hashes and the set is small.
        var postClickRows = await db.VisitEntities
            .Where(v => v.SocialPostCode!.LinkId == linkId && v.SocialPostCode.SocialPostId != null)
            .Select(v => new { PostId = v.SocialPostCode!.SocialPostId!.Value, v.HashedIp })
            .ToListAsync(ct);

        var byPost = postClickRows
            .GroupBy(r => r.PostId)
            .ToDictionary(
                g => g.Key,
                g => (Clicks: g.LongCount(), Unique: g.Select(x => x.HashedIp).Distinct().LongCount()));

        var organic = await db.VisitEntities
            .LongCountAsync(v => v.ShortCodeId != null && v.ShortCode!.LinkId == linkId, ct);

        return new LinkAttributionSplit(
            AttributedClicks: postClickRows.Count,
            OrganicClicks: organic,
            Posts: posts
                .Select(p =>
                {
                    var stats = byPost.GetValueOrDefault(p.Id);
                    return new PostClickCount(
                        p.Id, p.Platform.ToString(), p.Handle, p.PostUrl, p.PostedAt,
                        stats.Clicks, stats.Unique, p.Impressions, p.Likes);
                })
                .OrderByDescending(p => p.Clicks)
                .ToList());
    }

    /// <summary>Total clicks across every link in an account (all code types).</summary>
    public static async Task<long> CountForAccountAsync(
        ShortLynxDbContext db, Guid accountId, CancellationToken ct = default)
        => await db.VisitEntities.LongCountAsync(
               v => (v.ShortCodeId != null && v.ShortCode!.Link.AccountId == accountId)
                 || (v.SocialPostCodeId != null && v.SocialPostCode!.Link.AccountId == accountId), ct)
         + await db.UserVisitEntities.LongCountAsync(v => v.UserLinkCode.Link.AccountId == accountId, ct);

    /// <summary>
    /// Total clicks across every link in an account (all code types), restricted to
    /// <c>[from, to)</c> by <c>ClickedAt</c>. Same definition as <see cref="CountForAccountAsync"/> —
    /// used by the hosted billing repo to compute a billing period's redirect count for overage
    /// metering (HOSTED_BILLING_PLAN §9); self-host has no caller for this today.
    /// </summary>
    public static async Task<long> CountForAccountInRangeAsync(
        ShortLynxDbContext db, Guid accountId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var shortCodeIds = await db.ShortCodeEntities.Where(sc => sc.Link.AccountId == accountId).Select(sc => sc.Id).ToListAsync(ct);
        var socialPostCodeIds = await db.SocialPostCodeEntities.Where(sp => sp.Link.AccountId == accountId).Select(sp => sp.Id).ToListAsync(ct);
        var userLinkCodeIds = await db.UserLinkCodeEntities.Where(u => u.Link.AccountId == accountId).Select(u => u.Id).ToListAsync(ct);

        // SQLite (dev/tests) can't compare DateTimeOffset in SQL — same limitation
        // VisitRetentionService.PruneOnceAsync already works around: resolve the id-filtered rows'
        // ClickedAt values, then apply the range client-side. PostgreSQL takes the single-statement
        // fast path (the range pushed straight into the WHERE clause).
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var shortCodeClicks = await db.VisitEntities
                .Where(v => v.ShortCodeId != null && shortCodeIds.Contains(v.ShortCodeId.Value))
                .Select(v => v.ClickedAt).ToListAsync(ct);
            var socialPostClicks = await db.VisitEntities
                .Where(v => v.SocialPostCodeId != null && socialPostCodeIds.Contains(v.SocialPostCodeId.Value))
                .Select(v => v.ClickedAt).ToListAsync(ct);
            var userClicks = await db.UserVisitEntities
                .Where(v => userLinkCodeIds.Contains(v.UserLinkCodeId))
                .Select(v => v.ClickedAt).ToListAsync(ct);

            return shortCodeClicks.Count(c => c >= from && c < to)
                 + socialPostClicks.Count(c => c >= from && c < to)
                 + userClicks.Count(c => c >= from && c < to);
        }

        var shortCodeVisits = await db.VisitEntities.LongCountAsync(
            v => v.ClickedAt >= from && v.ClickedAt < to &&
                 v.ShortCodeId != null && shortCodeIds.Contains(v.ShortCodeId.Value), ct);

        var socialPostVisits = await db.VisitEntities.LongCountAsync(
            v => v.ClickedAt >= from && v.ClickedAt < to &&
                 v.SocialPostCodeId != null && socialPostCodeIds.Contains(v.SocialPostCodeId.Value), ct);

        var userVisits = await db.UserVisitEntities.LongCountAsync(
            v => v.ClickedAt >= from && v.ClickedAt < to && userLinkCodeIds.Contains(v.UserLinkCodeId), ct);

        return shortCodeVisits + socialPostVisits + userVisits;
    }
}
