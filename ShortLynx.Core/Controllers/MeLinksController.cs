using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Analytics;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Core.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Links;
using ShortLynx.Services.Qr;

namespace ShortLynx.Core.Controllers;

[Route("me/links")]
public class MeLinksController(
    ILinkService linkService, ShortLynxDbContext db,
    IQrCodeService qr, IOptions<LinkUrlOptions> linkOptions) : SessionControllerBase
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

    // PUT /me/links/{id}/campaign — assign/unassign the link to one of the account's campaigns.
    [HttpPut("{id:guid}/campaign")]
    public async Task<IActionResult> SetCampaign(Guid id, [FromBody] SetLinkCampaignRequest request, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        var ok = await linkService.SetLinkCampaignAsync(id, request.CampaignId, AccountId, ct);
        return ok ? NoContent() : BadRequest(new { error = "Campaign not found or not in this account." });
    }

    // GET /me/links/{id}/analytics
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();

        List<CodeClickStats> codeStats;
        List<VisitRow> rows;

        if (link.Mode == LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities.FirstOrDefaultAsync(x => x.LinkId == id, ct);
            if (sc is null)
            {
                codeStats = [];
                rows = [];
            }
            else
            {
                rows = (await db.VisitEntities
                        .Where(v => v.ShortCodeId == sc.Id)
                        .Select(v => new { v.HashedIp, v.Source, v.Device, v.ClickedAt })
                        .ToListAsync(ct))
                    .Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt))
                    .ToList();
                codeStats = [new CodeClickStats(sc.Code, null, rows.Count)];
            }
        }
        else
        {
            var codes = await db.UserLinkCodeEntities.Where(c => c.LinkId == id).ToListAsync(ct);
            var codeIds = codes.Select(c => c.Id).ToList();
            var visits = await db.UserVisitEntities
                .Where(v => codeIds.Contains(v.UserLinkCodeId))
                .Select(v => new { v.UserLinkCodeId, v.HashedIp, v.Source, v.Device, v.ClickedAt })
                .ToListAsync(ct);

            var countByCode = visits
                .GroupBy(v => v.UserLinkCodeId)
                .ToDictionary(g => g.Key, g => g.LongCount());
            codeStats = codes
                .Select(c => new CodeClickStats(c.Code, c.UserId, countByCode.GetValueOrDefault(c.Id, 0)))
                .ToList();
            rows = visits.Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt)).ToList();
        }

        var b = ClickAggregator.Summarize(rows);
        return Ok(new LinkAnalyticsResponse(
            id, link.OriginalUrl, link.Mode.ToString(),
            b.TotalClicks, b.UniqueClicks, b.FirstClickAt, b.LastClickAt,
            codeStats, b.Sources, b.Devices, b.Timeline));
    }

    // GET /me/links/{id}/qr?format=png|svg&size=<n>&code=<optional>
    // Returns a downloadable QR code that encodes the link's full short URL. For user-attributed links
    // (one code per recipient) pass ?code= to choose which code to encode.
    [HttpGet("{id:guid}/qr")]
    public async Task<IActionResult> Qr(
        Guid id, [FromQuery] string format = "png", [FromQuery] int size = 10,
        [FromQuery] string? code = null, CancellationToken ct = default)
    {
        var isPng = string.Equals(format, "png", StringComparison.OrdinalIgnoreCase);
        var isSvg = string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase);
        if (!isPng && !isSvg)
            return BadRequest(new { error = $"Unknown format '{format}'. Use 'png' or 'svg'." });

        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();

        var targetCode = await ResolveCodeAsync(link, code, ct);
        if (targetCode is null) return NotFound();

        var url = await ShortUrlBuilder.BuildAsync(db, link, targetCode, linkOptions.Value.PublicBaseUrl, ct);

        return isSvg
            ? File(Encoding.UTF8.GetBytes(qr.GenerateSvg(url, size)), "image/svg+xml", $"{targetCode}.svg")
            : File(qr.GeneratePng(url, size), "image/png", $"{targetCode}.png");
    }

    // Picks the code to encode: an explicit ?code= (validated against this link), else the anonymous
    // link's single short code. User-attributed links have no single code, so they require ?code=.
    private async Task<string?> ResolveCodeAsync(LinkEntity link, string? code, CancellationToken ct)
    {
        if (code is not null)
        {
            var belongs = link.Mode == LinkMode.Anonymous
                ? await db.ShortCodeEntities.AnyAsync(sc => sc.LinkId == link.Id && sc.Code == code, ct)
                : await db.UserLinkCodeEntities.AnyAsync(c => c.LinkId == link.Id && c.Code == code, ct);
            return belongs ? code : null;
        }

        if (link.Mode != LinkMode.Anonymous) return null;
        return await db.ShortCodeEntities.Where(sc => sc.LinkId == link.Id)
            .Select(sc => sc.Code).FirstOrDefaultAsync(ct);
    }

    private static LinkResponse ToLinkResponse(LinkEntity link, string shortCode)
        => new(link.Id, link.OriginalUrl, link.Mode.ToString(), shortCode, link.CreatedAt, link.ExpiresAt);
}
