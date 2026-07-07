using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Services.Analytics;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
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

        // Grouped client-side: SQLite (dev/tests) cannot translate Min over DateTimeOffset.
        var codeIds = codes.Select(c => c.Id).ToList();
        var firstClicks = (await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new { v.UserLinkCodeId, v.ClickedAt })
                .ToListAsync(ct))
            .GroupBy(v => v.UserLinkCodeId)
            .ToDictionary(g => g.Key, g => g.Min(v => v.ClickedAt));

        return RecipientEngagement.Compute(codes
            .Select(c => (c.CreatedAt, firstClicks.TryGetValue(c.Id, out var f) ? f : (DateTimeOffset?)null))
            .ToList());
    }

    // Resolves every visit for the given links, tagged with its link id. Anonymous links resolve via
    // their short code → Visits; user-attributed links via their per-recipient codes → UserVisits.
    private async Task<List<(Guid LinkId, VisitRow Row)>> GatherVisitsAsync(List<Guid> linkIds, CancellationToken ct)
    {
        var tagged = new List<(Guid, VisitRow)>();
        if (linkIds.Count == 0) return tagged;

        // Mode 1: short code → link.
        var shortCodeToLink = await db.ShortCodeEntities
            .Where(sc => linkIds.Contains(sc.LinkId))
            .Select(sc => new { sc.Id, sc.LinkId })
            .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        if (shortCodeToLink.Count > 0)
        {
            var scIds = shortCodeToLink.Keys.ToList();
            var visits = await db.VisitEntities
                .Where(v => scIds.Contains(v.ShortCodeId))
                .Select(v => new { v.ShortCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt })
                .ToListAsync(ct);
            tagged.AddRange(visits.Select(v =>
                (shortCodeToLink[v.ShortCodeId], new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt))));
        }

        // Mode 2: user-link code → link.
        var codeToLink = await db.UserLinkCodeEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new { c.Id, c.LinkId })
            .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        if (codeToLink.Count > 0)
        {
            var codeIds = codeToLink.Keys.ToList();
            var userVisits = await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new { v.UserLinkCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt })
                .ToListAsync(ct);
            tagged.AddRange(userVisits.Select(v =>
                (codeToLink[v.UserLinkCodeId], new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt))));
        }

        return tagged;
    }

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.CampaignId != null)
            .GroupBy(l => l.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);

    private static CampaignResponse ToResponse(CampaignEntity c, int linkCount) => new(
        c.Id, c.Name, c.Description, c.UtmSource, c.UtmMedium, c.UtmCampaign, linkCount, c.CreatedAt);
}
