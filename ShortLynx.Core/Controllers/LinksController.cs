using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Analytics;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Links;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.Tags;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("links")]
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class LinksController(
    ILinkService linkService, ITagService tagService, ShortLynxDbContext db,
    IOptions<AnalyticsOptions> analyticsOptions) : ControllerBase
{
    private int AnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? ClickAggregator.AnonymityThreshold : 0;
    private int CityAnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? CityAggregator.AnonymityThreshold : 0;

    private ApiKeyEntity CurrentKey => (ApiKeyEntity)HttpContext.Items["ApiKey"]!;

    // POST /links
    [HttpPost]
    [RequireScope(Scopes.LinksWrite)]
    public async Task<IActionResult> CreateLink(
        [FromBody] CreateLinkRequest request,
        CancellationToken ct)
    {
        if (request.TagIds is { Length: > 0 } &&
            !CurrentKey.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(Scopes.TagsWrite, StringComparer.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = $"This API key lacks the '{Scopes.TagsWrite}' scope." });
        }

        var isUserAttributed = string.Equals(request.Mode, nameof(LinkMode.UserAttributed), StringComparison.OrdinalIgnoreCase);
        if (isUserAttributed && !string.IsNullOrWhiteSpace(request.CustomCode))
            return BadRequest(new { error = "Custom codes are only available for anonymous links." });

        try
        {
            Guid linkId;
            LinkResponse response;

            if (isUserAttributed)
            {
                var link = await linkService.CreateUserAttributedLinkAsync(
                    request.Url, CurrentKey.AccountId, CurrentKey.UserAccountId, request.CampaignId, ct);
                linkId = link.Id;
                response = ToLinkResponse(link, string.Empty, false);
            }
            else
            {
                var result = await linkService.CreateAnonymousLinkAsync(
                    request.Url, CurrentKey, request.CustomCode, request.CampaignId, ct);
                linkId = result.Link.Id;
                response = ToLinkResponse(result.Link, result.ShortCode.Code, result.ShortCode.IsCustom);
            }

            if (request.TagIds is { Length: > 0 } tagIds)
                await tagService.SetLinkTagsAsync(linkId, tagIds, CurrentKey.AccountId, ct);

            return CreatedAtAction(nameof(GetLink), new { id = linkId }, response);
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
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = ex.Message });
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
            .Select(sc => new { sc.LinkId, sc.Code, sc.IsCustom })
            .ToListAsync(ct);

        var codeMap = codes
            .GroupBy(c => c.LinkId)
            .ToDictionary(g => g.Key, g => g.First());

        var items = links
            .Select(l =>
            {
                var c = codeMap.GetValueOrDefault(l.Id);
                return ToLinkResponse(l, c?.Code ?? string.Empty, c?.IsCustom ?? false);
            })
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

        var sc = await db.ShortCodeEntities
            .Where(x => x.LinkId == id)
            .Select(x => new { x.Code, x.IsCustom })
            .FirstOrDefaultAsync(ct);

        return Ok(ToLinkResponse(link, sc?.Code ?? string.Empty, sc?.IsCustom ?? false));
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

        var recipients = ResolveRecipients(request);
        if (recipients is null)
            return BadRequest(new { error = "Provide either userIds or recipients." });

        var codes = await linkService.CreateUserLinkCodesAsync(id, recipients, request.IsOneTimeUse, ct);

        var response = codes.Select(c => new UserCodeResponse(c.UserId, c.Code, c.Recipient, c.IsOneTimeUse)).ToList();
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

        // Shared canonical query layer (also used by MeLinksController.Analytics) -- this used to
        // reimplement a narrower version by hand (HashedIp/Source/Device/ClickedAt only), which meant
        // this endpoint's Browser/OS/language/country/UTM breakdowns would always come back empty
        // rather than actually reflecting Browser/OS/etc.
        var rows = await LinkVisitQueries.LoadLinkRowsAsync(db, link, ct);
        var codeStats = (await LinkVisitQueries.LoadCodeCountsAsync(db, link, ct))
            .Select(c => new CodeClickStats(c.Code, c.UserId, c.Clicks, c.Recipient))
            .ToList();

        var b = ClickAggregator.Summarize(rows, AnonymityThreshold);
        var cities = CityAggregator.Summarize(await CityClickQueries.LoadForLinksAsync(db, [id], ct), CityAnonymityThreshold);
        return Ok(new LinkAnalyticsResponse(
            id, link.OriginalUrl, link.Mode.ToString(),
            b.TotalClicks, b.UniqueClicks, b.HumanClicks, b.HumanUniqueClicks, b.BotClicks,
            b.FirstClickAt, b.LastClickAt,
            codeStats, b.Sources, b.Devices, b.Timeline, b.HourlyDistribution, cities,
            b.Browsers, b.OperatingSystems, b.Languages, b.Countries, b.NavigationTypes,
            b.UtmSources, b.UtmMediums, b.UtmCampaigns));
    }

    private static LinkResponse ToLinkResponse(LinkEntity link, string shortCode, bool isCustom) =>
        new(link.Id, link.OriginalUrl, link.Mode.ToString(), shortCode, link.CreatedAt, link.ExpiresAt,
            link.CampaignId, isCustom, link.CustomDomainId, link.FolderId, link.Nickname);

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
