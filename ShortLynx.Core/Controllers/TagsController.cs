using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Tags;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Programmatic tag management. Organizing API-created links by tag (e.g. "everything this week's
/// deploy created") is a plausible integration use case in a way UTM-templated campaigns aren't, so
/// unlike Campaigns/Folders, Tags get an API-key surface too.
/// </summary>
[ApiController]
[Route("tags")]
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class TagsController(ITagService tags) : ControllerBase
{
    private ApiKeyEntity CurrentKey => (ApiKeyEntity)HttpContext.Items["ApiKey"]!;
    private Guid AccountId => CurrentKey.AccountId;

    // GET /tags
    [HttpGet]
    [RequireScope(Scopes.TagsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await tags.ListAsync(AccountId, ct);
        return Ok(list.Select(t => new TagResponse(t.Id, t.Name, 0, t.CreatedAt)));
    }

    // POST /tags
    [HttpPost]
    [RequireScope(Scopes.TagsWrite)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest request, CancellationToken ct)
    {
        try
        {
            var tag = await tags.CreateAsync(AccountId, new TagInput(request.Name), ct);
            // No GET /tags/{id} on this API-key surface (only List + Create), so there's no route to
            // point CreatedAtAction at -- just return 201 with the created resource in the body.
            return StatusCode(StatusCodes.Status201Created, new TagResponse(tag.Id, tag.Name, 0, tag.CreatedAt));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
