using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Links;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("links")]
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class LinksController(ILinkService linkService, ShortLynxDbContext db) : ControllerBase
{
    private ApiKeyEntity CurrentKey => (ApiKeyEntity)HttpContext.Items["ApiKey"]!;

    // POST /links
    [HttpPost]
    [RequireScope(Scopes.LinksWrite)]
    public async Task<IActionResult> CreateLink(
        [FromBody] CreateLinkRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await linkService.CreateAnonymousLinkAsync(request.Url, CurrentKey, ct);
            var response = ToLinkResponse(result.Link, result.ShortCode.Code);
            return CreatedAtAction(nameof(GetLink), new { id = result.Link.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /links
    [HttpGet]
    [RequireScope(Scopes.LinksRead)]
    public async Task<IActionResult> ListLinks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Order by Id: v7 GUIDs are time-monotonic, so this is equivalent to ordering by
        // CreatedAt but avoids SQLite's DateTimeOffset-in-ORDER-BY limitation.
        var links = await db.LinkEntities
            .Where(l => l.AccountId == CurrentKey.AccountId)
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (links.Count == 0)
            return Ok(Array.Empty<LinkResponse>());

        var linkIds = links.Select(l => l.Id).ToHashSet();
        var codes = await db.ShortCodeEntities
            .Where(sc => linkIds.Contains(sc.LinkId))
            .Select(sc => new { sc.LinkId, sc.Code })
            .ToListAsync(ct);

        var codeMap = codes
            .GroupBy(c => c.LinkId)
            .ToDictionary(g => g.Key, g => g.First().Code);

        var items = links
            .Select(l => ToLinkResponse(l, codeMap.GetValueOrDefault(l.Id, string.Empty)))
            .ToList();

        return Ok(items);
    }

    // GET /links/{id}
    [HttpGet("{id:guid}")]
    [RequireScope(Scopes.LinksRead)]
    public async Task<IActionResult> GetLink(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities
            .Where(l => l.Id == id && l.AccountId == CurrentKey.AccountId)
            .FirstOrDefaultAsync(ct);

        if (link is null) return NotFound();

        var shortCode = await db.ShortCodeEntities
            .Where(sc => sc.LinkId == id)
            .Select(sc => sc.Code)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return Ok(ToLinkResponse(link, shortCode));
    }

    // POST /links/{id}/codes
    [HttpPost("{id:guid}/codes")]
    [RequireScope(Scopes.CodesWrite)]
    public async Task<IActionResult> CreateUserCodes(
        Guid id,
        [FromBody] CreateUserCodesRequest request,
        CancellationToken ct)
    {
        var link = await db.LinkEntities
            .Where(l => l.Id == id && l.AccountId == CurrentKey.AccountId)
            .FirstOrDefaultAsync(ct);

        if (link is null) return NotFound();

        var codes = await linkService.CreateUserLinkCodesAsync(id, request.UserIds, ct);

        var response = codes.Select(c => new UserCodeResponse(c.UserId, c.Code)).ToList();
        return Ok(response);
    }

    // PUT /links/{id}/domain — pin (or unpin) the link to a verified custom domain.
    [HttpPut("{id:guid}/domain")]
    [RequireScope(Scopes.LinksWrite)]
    public async Task<IActionResult> SetLinkDomain(
        Guid id,
        [FromBody] SetLinkDomainRequest request,
        CancellationToken ct)
    {
        var link = await db.LinkEntities
            .Where(l => l.Id == id && l.AccountId == CurrentKey.AccountId)
            .FirstOrDefaultAsync(ct);

        if (link is null) return NotFound();

        if (request.CustomDomainId is { } domainId)
        {
            var ownsVerified = await db.CustomDomainEntities.AnyAsync(
                d => d.Id == domainId
                  && d.AccountId == CurrentKey.AccountId
                  && d.VerificationStatus == DomainVerificationStatus.Verified, ct);
            if (!ownsVerified)
                return BadRequest(new { error = "Domain not found, not owned by this key's account, or not verified." });
        }

        link.CustomDomainId = request.CustomDomainId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET /links/{id}/analytics
    [HttpGet("{id:guid}/analytics")]
    [RequireScope(Scopes.AnalyticsRead)]
    public async Task<IActionResult> GetAnalytics(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities
            .Where(l => l.Id == id && l.AccountId == CurrentKey.AccountId)
            .FirstOrDefaultAsync(ct);

        if (link is null) return NotFound();

        List<CodeClickStats> codeStats;
        long totalClicks;
        DateTimeOffset? lastClickAt;

        if (link.Mode == LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities
                .Where(x => x.LinkId == id)
                .FirstOrDefaultAsync(ct);

            if (sc is null)
            {
                codeStats = [];
                totalClicks = 0;
                lastClickAt = null;
            }
            else
            {
                var visits = await db.VisitEntities
                    .Where(v => v.ShortCodeId == sc.Id)
                    .ToListAsync(ct);

                totalClicks = visits.Count;
                lastClickAt = visits.Count > 0
                    ? visits.Max(v => v.ClickedAt)
                    : null;
                codeStats = [new CodeClickStats(sc.Code, null, visits.Count)];
            }
        }
        else
        {
            var codes = await db.UserLinkCodeEntities
                .Where(c => c.LinkId == id)
                .ToListAsync(ct);

            var codeIds = codes.Select(c => c.Id).ToList();
            var clicksByCode = await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .GroupBy(v => v.UserLinkCodeId)
                .Select(g => new { g.Key, Count = (long)g.Count(), Last = g.Max(v => v.ClickedAt) })
                .ToListAsync(ct);

            var countMap = clicksByCode.ToDictionary(x => x.Key, x => x.Count);
            var lastMap = clicksByCode.ToDictionary(x => x.Key, x => x.Last);

            codeStats = codes.Select(c => new CodeClickStats(
                c.Code, c.UserId, countMap.GetValueOrDefault(c.Id, 0))).ToList();

            totalClicks = codeStats.Sum(s => s.ClickCount);
            lastClickAt = lastMap.Count > 0 ? lastMap.Values.Max() : null;
        }

        return Ok(new LinkAnalyticsResponse(id, link.OriginalUrl, link.Mode.ToString(),
            totalClicks, lastClickAt, codeStats));
    }

    private static LinkResponse ToLinkResponse(LinkEntity link, string shortCode) =>
        new(link.Id, link.OriginalUrl, link.Mode.ToString(), shortCode, link.CreatedAt, link.ExpiresAt);
}
