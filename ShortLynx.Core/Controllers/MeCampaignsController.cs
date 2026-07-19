using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Analytics;
using ShortLynx.Services.Campaigns;

namespace ShortLynx.Core.Controllers;

[Route("me/campaigns")]
public class MeCampaignsController(ICampaignService campaigns, ShortLynxDbContext db) : SessionControllerBase
{
    // GET /me/campaigns
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await campaigns.ListAsync(AccountId, ct);
        var counts = await LinkCountsAsync(ct);
        return Ok(list.Select(c => ToResponse(c, counts.GetValueOrDefault(c.Id, 0))));
    }

    // POST /me/campaigns
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        try
        {
            var campaign = await campaigns.CreateAsync(
                AccountId,
                new CampaignInput(request.Name, request.Description, request.UtmSource, request.UtmMedium, request.UtmCampaign),
                CurrentUserId, ct);
            return CreatedAtAction(nameof(Get), new { id = campaign.Id }, ToResponse(campaign, 0));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /me/campaigns/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var campaign = await campaigns.GetAsync(id, AccountId, ct);
        if (campaign is null) return NotFound();
        var count = await db.LinkEntities.CountAsync(l => l.CampaignId == id, ct);
        return Ok(ToResponse(campaign, count));
    }

    // PUT /me/campaigns/{id}
    [HttpPut("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCampaignRequest request, CancellationToken ct)
    {
        try
        {
            var campaign = await campaigns.UpdateAsync(
                id, AccountId,
                new CampaignInput(request.Name, request.Description, request.UtmSource, request.UtmMedium, request.UtmCampaign),
                ct);
            if (campaign is null) return NotFound();
            var count = await db.LinkEntities.CountAsync(l => l.CampaignId == id, ct);
            return Ok(ToResponse(campaign, count));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // DELETE /me/campaigns/{id} — unassigns the campaign's links, then deletes it.
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
        => await campaigns.DeleteAsync(id, AccountId, ct) ? NoContent() : NotFound();

    // GET /me/campaigns/{id}/analytics — clicks rolled up across every link in the campaign.
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, CancellationToken ct)
    {
        var campaign = await campaigns.GetAsync(id, AccountId, ct);
        if (campaign is null) return NotFound();

        var links = await db.LinkEntities
            .Where(l => l.CampaignId == id && l.AccountId == AccountId)
            .Select(l => new { l.Id, l.OriginalUrl, l.Mode })
            .ToListAsync(ct);

        // (linkId, row) pairs across both link modes, so we can roll up campaign-wide and per-link.
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

        var b = ClickAggregator.Summarize(tagged.Select(x => x.Row).ToList());
        var engagement = await RecipientEngagementAsync(links.Select(l => l.Id).ToList(), ct);
        return Ok(new CampaignAnalyticsResponse(
            campaign.Id, campaign.Name, links.Count,
            b.TotalClicks, b.UniqueClicks, b.HumanClicks, b.HumanUniqueClicks, b.BotClicks,
            b.FirstClickAt, b.LastClickAt,
            b.Sources, b.Devices, b.Timeline, b.HourlyDistribution,
            engagement.RecipientsTotal, engagement.RecipientsClicked,
            engagement.MedianTimeToFirstClickMinutes, engagement.P90TimeToFirstClickMinutes,
            perLink));
    }

    // GET /me/campaigns/{id}/analytics/export — the campaign-wide aggregate breakdown as CSV.
    // Aggregate-only by decision (MASTER_PLAN P2): there is deliberately no row-per-click export.
    [HttpGet("{id:guid}/analytics/export")]
    public async Task<IActionResult> AnalyticsExport(Guid id, CancellationToken ct)
    {
        var campaign = await campaigns.GetAsync(id, AccountId, ct);
        if (campaign is null) return NotFound();

        var linkIds = await db.LinkEntities
            .Where(l => l.CampaignId == id && l.AccountId == AccountId)
            .Select(l => l.Id)
            .ToListAsync(ct);
        var tagged = await GatherVisitsAsync(linkIds, ct);

        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(tagged.Select(x => x.Row).ToList()));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"campaign-{id}-analytics.csv");
    }

    // Mode 2 engagement across the campaign's user-attributed links: joins each provisioned code to
    // its earliest visit, then reduces via the pure RecipientEngagement helper.
    private async Task<RecipientEngagementStats> RecipientEngagementAsync(List<Guid> linkIds, CancellationToken ct)
    {
        if (linkIds.Count == 0) return RecipientEngagement.Compute([]);

        var codes = await db.UserLinkCodeEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new { c.Id, c.CreatedAt })
            .ToListAsync(ct);
        if (codes.Count == 0) return RecipientEngagement.Compute([]);

        // Grouped client-side: SQLite (dev/tests) cannot translate Min over DateTimeOffset. The same
        // rows give the per-recipient click count — repeat clicks here are exact and unbounded in time
        // (a recipient code identifies the person, so no hash and no dedup window is involved).
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

    // Every visit for the given links, tagged with its link id — see LinkVisitQueries for the rule.
    private async Task<List<(Guid LinkId, VisitRow Row)>> GatherVisitsAsync(List<Guid> linkIds, CancellationToken ct)
        => (await LinkVisitQueries.LoadRowsByLinkAsync(db, linkIds, ct))
            .Select(t => (t.LinkId, t.Row))
            .ToList();

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.CampaignId != null)
            .GroupBy(l => l.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);

    private static CampaignResponse ToResponse(CampaignEntity c, int linkCount) => new(
        c.Id, c.Name, c.Description, c.UtmSource, c.UtmMedium, c.UtmCampaign, linkCount, c.CreatedAt);
}
