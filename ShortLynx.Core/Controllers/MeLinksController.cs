using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Core.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Analytics;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Links;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.Qr;

namespace ShortLynx.Core.Controllers;

[Route("me/links")]
public class MeLinksController(
    ILinkService linkService, ShortLynxDbContext db,
    IQrCodeService qr, IOptions<LinkUrlOptions> linkOptions,
    IOptions<ShortCodeOptions> shortCodeOptions) : SessionControllerBase
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
                .Select(sc => new { sc.LinkId, sc.Code, sc.IsCustom })
                .ToListAsync(ct))
            .GroupBy(c => c.LinkId)
            .ToDictionary(g => g.Key, g => g.First());

        return Ok(links.Select(l =>
        {
            var c = codeMap.GetValueOrDefault(l.Id);
            return ToLinkResponse(l, c?.Code ?? string.Empty, c?.IsCustom ?? false);
        }));
    }

    // POST /me/links — create an anonymous (default) or user-attributed link in the current account.
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateMyLinkRequest request, CancellationToken ct)
    {
        var isUserAttributed = string.Equals(request.Mode, nameof(LinkMode.UserAttributed), StringComparison.OrdinalIgnoreCase);
        if (isUserAttributed && !string.IsNullOrWhiteSpace(request.CustomCode))
            return BadRequest(new { error = "Custom codes are only available for anonymous links." });

        try
        {
            if (isUserAttributed)
            {
                var link = await linkService.CreateUserAttributedLinkAsync(request.Url, AccountId, CurrentUserId, request.CampaignId, ct);
                return CreatedAtAction(nameof(Get), new { id = link.Id }, ToLinkResponse(link, string.Empty, false));
            }

            var result = await linkService.CreateAnonymousLinkAsync(request.Url, AccountId, CurrentUserId, request.CampaignId, request.CustomCode, ct);
            return CreatedAtAction(nameof(Get), new { id = result.Link.Id },
                ToLinkResponse(result.Link, result.ShortCode.Code, result.ShortCode.IsCustom));
        }
        catch (CustomCodeTakenException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (EntitlementException ex)
        {
            // Plan limit / feature not in tier — "Payment Required" signals an upgrade is needed.
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = ex.Message });
        }
    }

    // GET /me/links/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();
        var sc = await db.ShortCodeEntities.Where(x => x.LinkId == id)
            .Select(x => new { x.Code, x.IsCustom }).FirstOrDefaultAsync(ct);
        return Ok(ToLinkResponse(link, sc?.Code ?? "", sc?.IsCustom ?? false));
    }

    // POST /me/links/{id}/codes — provision user-attributed codes. Either userIds (bare, no labels,
    // never one-time — back-compat) or recipients (labelled, honours isOneTimeUse).
    [HttpPost("{id:guid}/codes")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> CreateCodes(Guid id, [FromBody] CreateUserCodesRequest request, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        var recipients = ResolveRecipients(request);
        if (recipients is null)
            return BadRequest(new { error = "Provide either userIds or recipients." });

        var codes = await linkService.CreateUserLinkCodesAsync(id, recipients, request.IsOneTimeUse, ct);
        return Ok(codes.Select(c => new UserCodeResponse(c.UserId, c.Code, c.Recipient, c.IsOneTimeUse)));
    }

    // PUT /me/links/{id}/domain — pin/unpin to a verified account domain.
    [HttpPut("{id:guid}/domain")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> SetDomain(Guid id, [FromBody] SetLinkDomainRequest request, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        var ok = await linkService.SetLinkDomainAsync(id, request.CustomDomainId, AccountId, ct);
        return ok ? NoContent() : BadRequest(new { error = "Domain not found, not in this account, or not verified." });
    }

    // PUT /me/links/{id}/campaign — assign/unassign the link to one of the account's campaigns.
    [HttpPut("{id:guid}/campaign")]
    [RequireAccountAction(AccountAction.ManageResources)]
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

        var rows = await LinkVisitQueries.LoadLinkRowsAsync(db, link, ct);
        var codeStats = (await LinkVisitQueries.LoadCodeCountsAsync(db, link, ct))
            .Select(c => new CodeClickStats(c.Code, c.UserId, c.Clicks, c.Recipient))
            .ToList();

        var b = ClickAggregator.Summarize(rows);
        return Ok(new LinkAnalyticsResponse(
            id, link.OriginalUrl, link.Mode.ToString(),
            b.TotalClicks, b.UniqueClicks, b.HumanClicks, b.HumanUniqueClicks, b.BotClicks,
            b.FirstClickAt, b.LastClickAt,
            codeStats, b.Sources, b.Devices, b.Timeline, b.HourlyDistribution));
    }

    // GET /me/links/{id}/analytics/export — the same aggregate breakdown as /analytics, as CSV.
    // Aggregate-only by decision (MASTER_PLAN P2): there is deliberately no row-per-click export.
    [HttpGet("{id:guid}/analytics/export")]
    public async Task<IActionResult> AnalyticsExport(Guid id, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();

        List<VisitRow> rows;
        if (link.Mode == LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities.FirstOrDefaultAsync(x => x.LinkId == id, ct);
            rows = sc is null
                ? []
                : (await db.VisitEntities.Where(v => v.ShortCodeId == sc.Id)
                        .Select(v => new { v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign })
                        .ToListAsync(ct))
                    .Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign))
                    .ToList();
        }
        else
        {
            var codeIds = await db.UserLinkCodeEntities.Where(c => c.LinkId == id).Select(c => c.Id).ToListAsync(ct);
            rows = (await db.UserVisitEntities.Where(v => codeIds.Contains(v.UserLinkCodeId))
                    .Select(v => new { v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign })
                    .ToListAsync(ct))
                .Select(v => new VisitRow(v.HashedIp, v.Source, v.Device, v.ClickedAt, v.Browser, v.Os, v.Country, v.Language, v.NavigationType, v.TimeZone, v.UtmSource, v.UtmMedium, v.UtmCampaign))
                .ToList();
        }

        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(rows));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"link-{id}-analytics.csv");
    }

    // POST /me/links/{id}/publish — post the link's short URL to connected social accounts.
    [HttpPost("{id:guid}/publish")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Publish(
        Guid id, [FromBody] PublishLinkRequest request,
        [FromServices] ShortLynx.Services.Social.ISocialPublishService publisher, CancellationToken ct)
    {
        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == id && l.AccountId == AccountId, ct);
        if (link is null) return NotFound();

        // Anonymous links only: user-attributed links exist to give each recipient their own code, and
        // a public broadcast has no recipient to attribute to.
        if (link.Mode != LinkMode.Anonymous)
            return BadRequest(new { error = "Only anonymous links can be published (user-attributed links have per-recipient codes)." });

        try
        {
            // The publisher mints a per-post code off this base URL, so it needs the base, not a
            // pre-resolved short URL.
            var results = await publisher.PublishLinkAsync(
                AccountId, id, request.ConnectionIds, request.Text, linkOptions.Value.PublicBaseUrl, ct);
            return Ok(results.Select(r => new PublishTargetResponse(
                r.ConnectionId, r.Handle, r.Success, r.Post?.PostUrl, r.Error)));
        }
        catch (EntitlementException ex)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = ex.Message });
        }
    }

    // GET /me/links/{id}/posts — publishing history (and, once pulled, metrics) for the link.
    [HttpGet("{id:guid}/posts")]
    public async Task<IActionResult> Posts(Guid id, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        return Ok(await LoadPostsAsync(id, ct));
    }

    // POST /me/links/{id}/posts/refresh — pull current engagement metrics now, then return the posts.
    [HttpPost("{id:guid}/posts/refresh")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> RefreshPosts(
        Guid id, [FromServices] ShortLynx.Services.Social.ISocialMetricsService metrics, CancellationToken ct)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == id && l.AccountId == AccountId, ct))
            return NotFound();

        await metrics.RefreshLinkAsync(AccountId, id, ct);
        return Ok(await LoadPostsAsync(id, ct));
    }

    private async Task<IEnumerable<SocialPostResponse>> LoadPostsAsync(Guid linkId, CancellationToken ct)
    {
        var posts = await db.SocialPostEntities
            .Where(p => p.LinkId == linkId)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        // Exact per-post clicks (each post has its own code) alongside the platform's engagement — so a
        // caller can compare "40 likes" against "1 click" without guessing from referrers.
        var clicksByPost = (await LinkVisitQueries.LoadAttributionSplitAsync(db, linkId, ct))
            .Posts.ToDictionary(p => p.SocialPostId, p => (p.Clicks, p.UniqueClicks));

        return posts.Select(p =>
        {
            var c = clicksByPost.GetValueOrDefault(p.Id);
            return new SocialPostResponse(
                p.Id, p.Platform.ToString(), p.Handle, p.PostUrl, p.Text, p.PostedAt,
                p.Impressions, p.Likes, p.Reposts, p.Replies, p.MetricsUpdatedAt,
                c.Clicks, c.UniqueClicks);
        });
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

        var resolved = await ResolveCodeAsync(link, code, ct);
        if (resolved is null) return NotFound();

        var url = await ShortUrlBuilder.BuildAsync(
            db, link, resolved.Value.Code, resolved.Value.IsCustom, shortCodeOptions.Value.CustomRoutePrefix,
            linkOptions.Value.PublicBaseUrl, ct);

        return isSvg
            ? File(Encoding.UTF8.GetBytes(qr.GenerateSvg(url, size)), "image/svg+xml", $"{resolved.Value.Code}.svg")
            : File(qr.GeneratePng(url, size), "image/png", $"{resolved.Value.Code}.png");
    }

    private readonly record struct ResolvedCode(string Code, bool IsCustom);

    // Picks the code to encode: an explicit ?code= (validated against this link), else the anonymous
    // link's single short code. User-attributed links have no single code, so they require ?code=.
    // Carries IsCustom alongside the code so ShortUrlBuilder can route it under the custom prefix —
    // a bare code lookup here previously fed a URL that 404s for vanity codes (RedirectService excludes
    // them from the root route by design).
    private async Task<ResolvedCode?> ResolveCodeAsync(LinkEntity link, string? code, CancellationToken ct)
    {
        if (code is not null)
        {
            if (link.Mode != LinkMode.Anonymous)
            {
                var belongs = await db.UserLinkCodeEntities.AnyAsync(c => c.LinkId == link.Id && c.Code == code, ct);
                return belongs ? new ResolvedCode(code, false) : null;
            }

            var match = await db.ShortCodeEntities
                .Where(sc => sc.LinkId == link.Id && sc.Code == code)
                .Select(sc => new { sc.IsCustom })
                .FirstOrDefaultAsync(ct);
            return match is null ? null : new ResolvedCode(code, match.IsCustom);
        }

        if (link.Mode != LinkMode.Anonymous) return null;
        var sc2 = await db.ShortCodeEntities.Where(sc => sc.LinkId == link.Id)
            .Select(sc => new { sc.Code, sc.IsCustom })
            .FirstOrDefaultAsync(ct);
        return sc2 is null ? null : new ResolvedCode(sc2.Code, sc2.IsCustom);
    }

    private static LinkResponse ToLinkResponse(LinkEntity link, string shortCode, bool isCustom)
        => new(link.Id, link.OriginalUrl, link.Mode.ToString(), shortCode, link.CreatedAt, link.ExpiresAt,
               link.CampaignId, isCustom, link.CustomDomainId);

    // Null means neither field was usably supplied — the caller returns 400.
    private static IReadOnlyCollection<CodeRecipient>? ResolveRecipients(CreateUserCodesRequest request)
    {
        if (request.Recipients is { Length: > 0 })
            return request.Recipients.Select(r => new CodeRecipient(r.UserId, r.Recipient)).ToList();
        if (request.UserIds is { Length: > 0 })
            return request.UserIds.Select(id => new CodeRecipient(id)).ToList();
        return null;
    }
}
