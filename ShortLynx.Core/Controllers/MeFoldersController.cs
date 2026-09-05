using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Folders;

namespace ShortLynx.Core.Controllers;

[Route("me/folders")]
public class MeFoldersController(IFolderService folders, ShortLynxDbContext db) : SessionControllerBase
{
    // GET /me/folders
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await folders.ListAsync(AccountId, ct);
        var counts = await LinkCountsAsync(ct);
        return Ok(list.Select(f => ToResponse(f, counts.GetValueOrDefault(f.Id, 0))));
    }

    // POST /me/folders
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken ct)
    {
        try
        {
            var folder = await folders.CreateAsync(AccountId, new FolderInput(request.Name), CurrentUserId, ct);
            return CreatedAtAction(nameof(Get), new { id = folder.Id }, ToResponse(folder, 0));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // GET /me/folders/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var folder = await folders.GetAsync(id, AccountId, ct);
        if (folder is null) return NotFound();
        var count = await db.LinkEntities.CountAsync(l => l.FolderId == id, ct);
        return Ok(ToResponse(folder, count));
    }

    // PUT /me/folders/{id}
    [HttpPut("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest request, CancellationToken ct)
    {
        try
        {
            var folder = await folders.UpdateAsync(id, AccountId, new FolderInput(request.Name), ct);
            if (folder is null) return NotFound();
            var count = await db.LinkEntities.CountAsync(l => l.FolderId == id, ct);
            return Ok(ToResponse(folder, count));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    // DELETE /me/folders/{id} — unassigns the folder's links, then deletes it.
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
        => await folders.DeleteAsync(id, AccountId, ct) ? NoContent() : NotFound();

    private async Task<Dictionary<Guid, int>> LinkCountsAsync(CancellationToken ct)
        => await db.LinkEntities
            .Where(l => l.AccountId == AccountId && l.FolderId != null)
            .GroupBy(l => l.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

    private static FolderResponse ToResponse(FolderEntity f, int linkCount) => new(f.Id, f.Name, linkCount, f.CreatedAt);
}
