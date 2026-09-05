using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Analytics;
using ShortLynx.Services.Folders;

namespace ShortLynx.Core.Controllers;

[Route("me/folders")]
public class MeFoldersController(
    IFolderService folders, ShortLynxDbContext db, IOptions<AnalyticsOptions> analyticsOptions) : SessionControllerBase
{
    private int AnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? ClickAggregator.AnonymityThreshold : 0;
    private int CityAnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? CityAggregator.AnonymityThreshold : 0;


    // GET /me/folders
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await folders.ListAsync(AccountId, ct);
        var counts = await LinkCountsAsync(ct);
        return Ok(list.Select(f => ToResponse(f, counts.GetValueOrDefault(f.Id, 0))));
    }

    // POST /me/folders
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken ct)
    {
        try
        {
            var folder = await folders.CreateAsync(AccountId, new FolderInput(request.Name), CurrentUserId, ct);
            return CreatedAtAction(nameof(Get), new { id = folder.Id }, ToResponse(folder, 0));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /me/folders/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var folder = await folders.GetAsync(id, AccountId, ct);
        if (folder is null) return NotFound();
        var count = await db.LinkEntities.CountAsync(l => l.FolderId == id, ct);
        return Ok(ToResponse(folder, count));
    }

    // PUT /me/folders/{id}
    [HttpPut("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest request, CancellationToken ct)
    {
        try
        {
            var folder = await folders.UpdateAsync(id, AccountId, new FolderInput(request.Name), ct);
            if (folder is null) return NotFound();
            var count = await db.LinkEntities.CountAsync(l => l.FolderId == id, ct);
            return Ok(ToResponse(folder, count));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // DELETE /me/folders/{id} — unassigns the folder's links, then deletes it.
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
        => await folders.DeleteAsync(id, AccountId, ct) ? NoContent() : NotFound();

    // GET /me/folders/{id}/analytics — clicks rolled up across every link in the folder. Near-verbatim
    // copy of MeCampaignsController.Analytics: a folder is a single-parent grouping just like a
    // campaign, so the same join/aggregate shape applies unchanged.
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, CancellationToken ct)
    {
        var folder = await folders.GetAsync(id, AccountId, ct);
        if (folder is null) return NotFound();

        var links = await db.LinkEntities
            .Where(l => l.FolderId == id && l.AccountId == AccountId)
            .Select(l => new { l.Id, l.OriginalUrl, l.Mode })
            .ToListAsync(ct);

        var tagged = await GatherVisitsAsync(links.Select(l => l.Id).ToList(), ct);

        var byLink = tagged.ToLookup(x => x.LinkId, x => x.Row);
        var perLink = links
            .Select(l =>
            {
                var rows = byLink[l.Id].ToList();
                return new CampaignLinkClicks(
                    l.Id, l.OriginalUrl, l.Mode.ToString(),
                    rows.Count, rows.Select(r => r.HashedIp).Distinct().Count());
            })
            .OrderByDescending(l => l.TotalClicks)
            .ToList();

        var b = ClickAggregator.Summarize(tagged.Select(x => x.Row).ToList(), AnonymityThreshold);
        var cities = CityAggregator.Summarize(
            await CityClickQueries.LoadForLinksAsync(db, links.Select(l => l.Id).ToList(), ct), CityAnonymityThreshold);
        var engagement = await RecipientEngagementAsync(links.Select(l => l.Id).ToList(), ct);
        return Ok(new FolderAnalyticsResponse(
            folder.Id, folder.Name, links.Count,
            b.TotalClicks, b.UniqueClicks, b.HumanClicks, b.HumanUniqueClicks, b.BotClicks,
            b.FirstClickAt, b.LastClickAt,
            b.Sources, b.Devices, b.Timeline, b.HourlyDistribution,
            engagement.RecipientsTotal, engagement.RecipientsClicked,
            engagement.MedianTimeToFirstClickMinutes, engagement.P90TimeToFirstClickMinutes,
            perLink, cities,
            b.Browsers, b.OperatingSystems, b.Languages, b.Countries, b.NavigationTypes,
            b.UtmSources, b.UtmMediums, b.UtmCampaigns));
    }

    // GET /me/folders/{id}/analytics/export — the folder-wide aggregate breakdown as CSV.
    [HttpGet("{id:guid}/analytics/export")]
    public async Task<IActionResult> AnalyticsExport(Guid id, CancellationToken ct)
    {
        var folder = await folders.GetAsync(id, AccountId, ct);
        if (folder is null) return NotFound();

        var linkIds = await db.LinkEntities
            .Where(l => l.FolderId == id && l.AccountId == AccountId)
            .Select(l => l.Id)
            .ToListAsync(ct);
        var tagged = await GatherVisitsAsync(linkIds, ct);

        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(tagged.Select(x => x.Row).ToList(), AnonymityThreshold));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"folder-{id}-analytics.csv");
    }

    // Mode 2 engagement across the folder's user-attributed links — identical helper to
    // MeCampaignsController's, generic over any link-id set.
    private async Task<RecipientEngagementStats> RecipientEngagementAsync(List<Guid> linkIds, CancellationToken ct)
    {
        if (linkIds.Count == 0) return RecipientEngagement.Compute([]);

        var codes = await db.UserLinkCodeEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new { c.Id, c.CreatedAt })
            .ToListAsync(ct);
        if (codes.Count == 0) return RecipientEngagement.Compute([]);

        var codeIds = codes.Select(c => c.Id).ToList();
        var byCode = (await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new { v.UserLinkCodeId, v.ClickedAt })
                .ToListAsync(ct))
            .GroupBy(v => v.UserLinkCodeId)
            .ToDictionary(g => g.Key, g => (First: g.Min(v => v.ClickedAt), Clicks: g.LongCount()));

        return RecipientEngagement.Compute(codes
            .Select(c => byCode.TryGetValue(c.Id, out var s)
                ? (c.CreatedAt, (DateTimeOffset?)s.First, s.Clicks)
                : (c.CreatedAt, null, 0L))
            .ToList());
    }

    private async Task<List<(Guid LinkId, VisitRow Row)>> GatherVisitsAsync(List<Guid> linkIds, CancellationToken ct)
        => (await LinkVisitQueries.LoadRowsByLinkAsync(db, linkIds, ct))
            .Select(t => (t.LinkId, t.Row))
            .ToList();

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.FolderId != null)
            .GroupBy(l => l.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

    private static FolderResponse ToResponse(FolderEntity f, int linkCount) => new(f.Id, f.Name, linkCount, f.CreatedAt);
}
