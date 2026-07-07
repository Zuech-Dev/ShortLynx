using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// Loads the full-dimension <see cref="VisitRow"/> projection for a link or a campaign, for the
/// aggregate CSV export. Query-only; the caller reduces via <see cref="ClickAggregator.Summarize"/>
/// (which applies the k=10 fold) and formats via <see cref="ClickBreakdownCsv"/>.
/// </summary>
public static class AnalyticsExportQueries
{
    public static async Task<List<VisitRow>> LoadLinkRowsAsync(ShortLynxDbContext db, LinkEntity link, CancellationToken ct = default)
    {
        if (link.Mode == LinkMode.Anonymous)
        {
            var scIds = await db.ShortCodeEntities.Where(s => s.LinkId == link.Id).Select(s => s.Id).ToListAsync(ct);
            return (await db.VisitEntities.Where(v => scIds.Contains(v.ShortCodeId))
                    .Select(v => new { v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign })
                    .ToListAsync(ct))
                .Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign))
                .ToList();
        }

        var codeIds = await db.UserLinkCodeEntities.Where(c => c.LinkId == link.Id).Select(c => c.Id).ToListAsync(ct);
        return (await db.UserVisitEntities.Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new { v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign })
                .ToListAsync(ct))
            .Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign))
            .ToList();
    }

    public static async Task<List<VisitRow>> LoadCampaignRowsAsync(ShortLynxDbContext db, Guid campaignId, Guid accountId, CancellationToken ct = default)
    {
        var links = await db.LinkEntities
            .Where(l => l.CampaignId == campaignId && l.AccountId == accountId)
            .ToListAsync(ct);

        var rows = new List<VisitRow>();
        foreach (var link in links)
            rows.AddRange(await LoadLinkRowsAsync(db, link, ct));
        return rows;
    }
}
