using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Analytics;

/// <summary>A visit tagged with the link it belongs to, for callers that query several links at once.</summary>
public readonly record struct LinkVisitRow(Guid LinkId, VisitRow Row);

/// <summary>Clicks on one code of a link (the shared code, or one recipient's code).</summary>
public readonly record struct CodeClickCount(Guid CodeId, string Code, Guid? UserId, long Clicks);

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
                .Where(v => scIds.Contains(v.ShortCodeId))
                .Select(v => new
                {
                    v.ShortCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os,
                    v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium,
                    v.UtmCampaign, v.ReferrerHost,
                })
                .ToListAsync(ct);

            tagged.AddRange(visits.Select(v => new LinkVisitRow(
                shortCodeToLink[v.ShortCodeId],
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

            var clicks = await db.VisitEntities.LongCountAsync(v => v.ShortCodeId == sc.Id, ct);
            return [new CodeClickCount(sc.Id, sc.Code, null, clicks)];
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
            .Where(v => linkIds.Contains(v.ShortCode.LinkId))
            .GroupBy(v => v.ShortCode.LinkId)
            .Select(g => new { LinkId = g.Key, Clicks = g.LongCount() })
            .ToListAsync(ct);
        foreach (var s in shared) counts[s.LinkId] = s.Clicks;

        var recipient = await db.UserVisitEntities
            .Where(v => linkIds.Contains(v.UserLinkCode.LinkId))
            .GroupBy(v => v.UserLinkCode.LinkId)
            .Select(g => new { LinkId = g.Key, Clicks = g.LongCount() })
            .ToListAsync(ct);
        foreach (var r in recipient)
            counts[r.LinkId] = counts.GetValueOrDefault(r.LinkId) + r.Clicks;

        return counts;
    }

    /// <summary>Total clicks across every link in an account.</summary>
    public static async Task<long> CountForAccountAsync(
        ShortLynxDbContext db, Guid accountId, CancellationToken ct = default)
        => await db.VisitEntities.LongCountAsync(v => v.ShortCode.Link.AccountId == accountId, ct)
         + await db.UserVisitEntities.LongCountAsync(v => v.UserLinkCode.Link.AccountId == accountId, ct);
}
