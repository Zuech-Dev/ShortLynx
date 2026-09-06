using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Campaigns;

namespace ShortLynx.Core.Controllers;

// Read-only API-key surface for campaigns (e.g. so a third-party client can populate a campaign
// picker before calling POST /links with a CampaignId). Creating/editing campaigns is dashboard-only
// for now, so there's no campaigns:write scope yet.
[ApiController]
[Route("campaigns")]
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class CampaignsController(ICampaignService campaigns, ShortLynxDbContext db) : ControllerBase
{
    private ApiKeyEntity CurrentKey => (ApiKeyEntity)HttpContext.Items["ApiKey"]!;
    private Guid AccountId => CurrentKey.AccountId;

    // GET /campaigns
    [HttpGet]
    [RequireScope(Scopes.CampaignsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await campaigns.ListAsync(AccountId, ct);
        var counts = await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.CampaignId != null)
            .GroupBy(l => l.CampaignId!.Value)
            .Select(g => new { CampaignId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);
        return Ok(list.Select(c => ToResponse(c, counts.GetValueOrDefault(c.Id, 0))));
    }

    // GET /campaigns/{id}
    [HttpGet("{id:guid}")]
    [RequireScope(Scopes.CampaignsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var campaign = await campaigns.GetAsync(id, AccountId, ct);
        if (campaign is null) return NotFound();
        var count = await db.LinkEntities.CountAsync(l => l.CampaignId == id, ct);
        return Ok(ToResponse(campaign, count));
    }

    private static CampaignResponse ToResponse(CampaignEntity c, int linkCount) => new(
        c.Id, c.Name, c.Description, c.UtmSource, c.UtmMedium, c.UtmCampaign, linkCount, c.CreatedAt);
}
