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
using ShortLynx.Services.Tags;

namespace ShortLynx.Core.Controllers;

[Route("me/tags")]
public class MeTagsController(
    ITagService tags, ShortLynxDbContext db, IOptions<AnalyticsOptions> analyticsOptions) : SessionControllerBase
{
    private int AnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? ClickAggregator.AnonymityThreshold : 0;
    private int CityAnonymityThreshold => analyticsOptions.Value.EnforceAnonymity ? CityAggregator.AnonymityThreshold : 0;


    // GET /me/tags
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await tags.ListAsync(AccountId, ct);
        var counts = await LinkCountsAsync(ct);
        return Ok(list.Select(t => ToResponse(t, counts.GetValueOrDefault(t.Id, 0))));
    }

    // POST /me/tags
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest request, CancellationToken ct)
    {
        try
        {
            var tag = await tags.CreateAsync(AccountId, new TagInput(request.Name), ct);
            return CreatedAtAction(nameof(Get), new { id = tag.Id }, ToResponse(tag, 0));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // GET /me/tags/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tag = await tags.GetAsync(id, AccountId, ct);
        if (tag is null) return NotFound();
        var count = await db.LinkTagEntities.CountAsync(lt => lt.TagId == id, ct);
        return Ok(ToResponse(tag, count));
    }

    // PUT /me/tags/{id}
    [HttpPut("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTagRequest request, CancellationToken ct)
    {
        try
        {
            var tag = await tags.UpdateAsync(id, AccountId, new TagInput(request.Name), ct);
            if (tag is null) return NotFound();
            var count = await db.LinkTagEntities.CountAsync(lt => lt.TagId == id, ct);
            return Ok(ToResponse(tag, count));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // DELETE /me/tags/{id} — LinkTagEntity rows cascade-delete with it.
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
        => await tags.DeleteAsync(id, AccountId, ct) ? NoContent() : NotFound();

    // GET /me/tags/{id}/analytics — clicks rolled up across every link carrying the tag. Fan-out join
    // (a link with several tags contributes to each independently) instead of Folder/Campaign's
    // single-parent one, but the aggregation pipeline downstream is identical.
    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(Guid id, CancellationToken ct)
    {
        var tag = await tags.GetAsync(id, AccountId, ct);
        if (tag is null) return NotFound();

        var links = await TaggedLinksAsync(id, ct);
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
        return Ok(new TagAnalyticsResponse(
            tag.Id, tag.Name, links.Count,
            b.TotalClicks, b.UniqueClicks, b.HumanClicks, b.HumanUniqueClicks, b.BotClicks,
            b.FirstClickAt, b.LastClickAt,
            b.Sources, b.Devices, b.Timeline, b.HourlyDistribution,
            engagement.RecipientsTotal, engagement.RecipientsClicked,
            engagement.MedianTimeToFirstClickMinutes, engagement.P90TimeToFirstClickMinutes,
            perLink, cities,
            b.Browsers, b.OperatingSystems, b.Languages, b.Countries, b.NavigationTypes,
            b.UtmSources, b.UtmMediums, b.UtmCampaigns));
    }

    // GET /me/tags/{id}/analytics/export — the tag-wide aggregate breakdown as CSV.
    [HttpGet("{id:guid}/analytics/export")]
    public async Task<IActionResult> AnalyticsExport(Guid id, CancellationToken ct)
    {
        var tag = await tags.GetAsync(id, AccountId, ct);
        if (tag is null) return NotFound();

        var linkIds = (await TaggedLinksAsync(id, ct)).Select(l => l.Id).ToList();
        var tagged = await GatherVisitsAsync(linkIds, ct);

        var csv = ClickBreakdownCsv.Format(ClickAggregator.Summarize(tagged.Select(x => x.Row).ToList(), AnonymityThreshold));
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"tag-{id}-analytics.csv");
    }

    private Task<List<TaggedLink>> TaggedLinksAsync(Guid tagId, CancellationToken ct)
        => db.LinkTagEntities
            .Where(lt => lt.TagId == tagId)
            .Join(db.LinkEntities.Where(l => l.AccountId == AccountId),
                lt => lt.LinkId, l => l.Id, (lt, l) => new TaggedLink(l.Id, l.OriginalUrl, l.Mode))
            .ToListAsync(ct);

    private readonly record struct TaggedLink(Guid Id, string OriginalUrl, ShortLynx.Data.Enums.LinkMode Mode);

    // Mode 2 engagement across the tag's user-attributed links — identical helper to
    // MeCampaignsController's/MeFoldersController's, generic over any link-id set.
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
        => await db.LinkTagEntities
            .Join(db.LinkEntities, lt => lt.LinkId, l => l.Id, (lt, l) => new { lt.TagId, l.AccountId })
            .Where(x => x.AccountId == AccountId)
            .GroupBy(x => x.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, ct);

    private static TagResponse ToResponse(TagEntity t, int linkCount) => new(t.Id, t.Name, linkCount, t.CreatedAt);
}
