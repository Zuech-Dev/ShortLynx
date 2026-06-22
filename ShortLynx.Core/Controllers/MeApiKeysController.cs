using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Core.Controllers;

[Route("me/api-keys")]
public class MeApiKeysController(IApiKeyService apiKeys, ShortLynxDbContext db) : SessionControllerBase
{
    // GET /me/api-keys
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var keys = await db.ApiKeyEntities
            .Where(k => k.AccountId == AccountId)
            .OrderByDescending(k => k.Id)
            .ToListAsync(ct);

        return Ok(keys.Select(k => new MyApiKeyResponse(
            k.Id, k.Name, k.Prefix,
            k.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            k.IsActive, k.CreatedAt)));
    }

    // POST /me/api-keys — mints a key for the current account; returns the plaintext once.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMyApiKeyRequest request, CancellationToken ct)
    {
        var (record, plaintext) = await apiKeys.CreateAsync(request.Name, request.Scopes ?? [], AccountId, CurrentUserId, ct);
        return Ok(new ApiKeyResponse(
            record.Id, record.Name, plaintext,
            record.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            record.CreatedAt));
    }

    // DELETE /me/api-keys/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
        => await apiKeys.RevokeAsync(id, AccountId, ct) ? NoContent() : NotFound();
}
