using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Tags;

namespace ShortLynx.Core.Controllers;

[Route("me/tags")]
public class MeTagsController(ITagService tags, ShortLynxDbContext db) : SessionControllerBase
{
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

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkTagEntities
            .Join(db.LinkEntities, lt => lt.LinkId, l => l.Id, (lt, l) => new { lt.TagId, l.AccountId })
            .Where(x => x.AccountId == AccountId)
            .GroupBy(x => x.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TagId, x => x.Count, ct);

    private static TagResponse ToResponse(TagEntity t, int linkCount) => new(t.Id, t.Name, linkCount, t.CreatedAt);
}
