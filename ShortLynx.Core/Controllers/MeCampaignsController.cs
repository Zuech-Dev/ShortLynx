using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.CampaignId != null)
            .GroupBy(l => l.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);

    private static CampaignResponse ToResponse(CampaignEntity c, int linkCount) => new(
        c.Id, c.Name, c.Description, c.UtmSource, c.UtmMedium, c.UtmCampaign, linkCount, c.CreatedAt);
}
