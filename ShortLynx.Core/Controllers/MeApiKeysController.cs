using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Services.Accounts;
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
    // ManageResources-gated: an API key acts with whatever scopes it was minted with, role-blind, so
    // letting a Viewer mint one would bypass their read-only role entirely.
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Create([FromBody] CreateMyApiKeyRequest request, CancellationToken ct)
    {
        var requested = (request.Scopes ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (requested.Length == 0)
            return BadRequest(new { error = "At least one scope is required." });

        var unknown = requested.Where(s => !Scopes.All.Contains(s, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
            return BadRequest(new { error = $"Unknown scope(s): {string.Join(", ", unknown)}." });

        var (record, plaintext) = await apiKeys.CreateAsync(request.Name, requested, AccountId, CurrentUserId, ct);
        return Ok(new ApiKeyResponse(
            record.Id, record.Name, plaintext,
            record.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            record.CreatedAt));
    }

    // DELETE /me/api-keys/{id}
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
        => await apiKeys.RevokeAsync(id, AccountId, ct) ? NoContent() : NotFound();
}
