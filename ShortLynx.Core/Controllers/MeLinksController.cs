using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Links;

namespace ShortLynx.Core.Controllers;

[Route("me/links")]
public class MeLinksController(ILinkService linkService, ShortLynxDbContext db) : SessionControllerBase
{
    // GET /me/links
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var links = await db.LinkEntities
            .Where(l => l.AccountId == AccountId)
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        if (links.Count == 0) return Ok(Array.Empty<LinkResponse>());

        var linkIds = links.Select(l => l.Id).ToHashSet();
        var codeMap = (await db.ShortCodeEntities
                .Where(sc => linkIds.Contains(sc.LinkId))
                .Select(sc => new { sc.LinkId, sc.Code })
                .ToListAsync(ct))
            .GroupBy(c => c.LinkId)
            .ToDictionary(g => g.Key, g => g.First().Code);

        return Ok(links.Select(l => ToLinkResponse(l, codeMap.GetValueOrDefault(l.Id, string.Empty))));
    }

    // POST /me/links — create an anonymous (default) or user-attributed link in the current account.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMyLinkRequest request, CancellationToken ct)
    {
        try
        {
            if (string.Equals(request.Mode, nameof(LinkMode.UserAttributed), StringComparison.OrdinalIgnoreCase))
            {
                var link = await linkService.CreateUserAttributedLinkAsync(request.Url, AccountId, CurrentUserId, ct);
                return CreatedAtAction(nameof(Get), new { id = link.Id }, ToLinkResponse(link, string.Empty));
            }

            var result = await linkService.CreateAnonymousLinkAsync(request.Url, AccountId, CurrentUserId, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Link.Id }, ToLinkResponse(result.Link, result.ShortCode.Code));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /me/links/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();
        var code = await db.ShortCodeEntities.Where(sc => sc.LinkId == id).Select(sc => sc.Code).FirstOrDefaultAsync(ct) ?? "";
        return Ok(ToLinkResponse(link, code));
    }

    // POST /me/links/{id}/codes — provision user-attributed codes.
    [HttpPost("{id:guid}/codes")]
    public async Task<IActionResult> CreateCodes(Guid id, [FromBody] CreateUserCodesRequest request, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        var codes = await linkService.CreateUserLinkCodesAsync(id, request.UserIds, ct);
        return Ok(codes.Select(c => new UserCodeResponse(c.UserId, c.Code)));
    }

    // PUT /me/links/{id}/domain — pin/unpin to a verified account domain.
    [HttpPut("{id:guid}/domain")]
    public async Task<IActionResult> SetDomain(Guid id, [FromBody] SetLinkDomainRequest request, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        var ok = await linkService.SetLinkDomainAsync(id, request.CustomDomainId, AccountId, ct);
        return ok ? NoContent() : BadRequest(new { error = "Domain not found, not in this account, or not verified." });
    }

    // GET /me/links/{id}/analytics
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();

        List<CodeClickStats> codeStats;
        long totalClicks;
        DateTimeOffset? lastClickAt;

        if (link.Mode == LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities.FirstOrDefaultAsync(x => x.LinkId == id, ct);
            if (sc is null)
            {
                codeStats = []; totalClicks = 0; lastClickAt = null;
            }
            else
            {
                var visits = await db.VisitEntities.Where(v => v.ShortCodeId == sc.Id).ToListAsync(ct);
                totalClicks = visits.Count;
                lastClickAt = visits.Count > 0 ? visits.Max(v => v.ClickedAt) : null;
                codeStats = [new CodeClickStats(sc.Code, null, visits.Count)];
            }
        }
        else
        {
            var codes = await db.UserLinkCodeEntities.Where(c => c.LinkId == id).ToListAsync(ct);
            var codeIds = codes.Select(c => c.Id).ToList();
            var clicksByCode = await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .GroupBy(v => v.UserLinkCodeId)
                .Select(g => new { g.Key, Count = (long)g.Count(), Last = g.Max(v => v.ClickedAt) })
                .ToListAsync(ct);

            var countMap = clicksByCode.ToDictionary(x => x.Key, x => x.Count);
            var lastMap = clicksByCode.ToDictionary(x => x.Key, x => x.Last);
            codeStats = codes.Select(c => new CodeClickStats(c.Code, c.UserId, countMap.GetValueOrDefault(c.Id, 0))).ToList();
            totalClicks = codeStats.Sum(s => s.ClickCount);
            lastClickAt = lastMap.Count > 0 ? lastMap.Values.Max() : null;
        }

        return Ok(new LinkAnalyticsResponse(id, link.OriginalUrl, link.Mode.ToString(), totalClicks, lastClickAt, codeStats));
    }

    private static LinkResponse ToLinkResponse(LinkEntity link, string shortCode)
        => new(link.Id, link.OriginalUrl, link.Mode.ToString(), shortCode, link.CreatedAt, link.ExpiresAt);
}
