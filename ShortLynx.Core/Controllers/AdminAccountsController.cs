using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Platform account administration for super-admins (is_admin) — cross-tenant, unlike the Owner-gated
/// <c>/me/account</c>. Exists so a super-admin can populate an account picker (<c>/admin/users</c>'s
/// "assign to an existing account" flow) and fix an account's own settings directly during support/
/// beta troubleshooting, without impersonating that account's Owner.
/// </summary>
[ApiController]
[Route("admin/accounts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = AuthorizationPolicies.SuperAdmin)]
public class AdminAccountsController(IAccountService accounts, ShortLynxDbContext db) : ControllerBase
{
    // GET /admin/accounts — every account, name only. A picker source, not a settings view.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await db.AccountEntities
            .OrderBy(a => a.Name)
            .Select(a => new AdminAccountSummaryResponse(a.Id, a.Name))
            .ToListAsync(ct);
        return Ok(list);
    }

    // GET /admin/accounts/{id} — full settings, to pre-fill the edit form.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var account = await accounts.GetAccountAsync(id, ct);
        return account is null ? NotFound() : Ok(ToResponse(account));
    }

    // PUT /admin/accounts/{id} — same validation as the Owner-gated /me/account (reuses
    // IAccountService.UpdateAccountAsync directly), just without requiring the actor to be a member of
    // the account being edited. The SuperAdmin policy on this controller is the actual gate.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAccountRequest request, CancellationToken ct)
    {
        try
        {
            var account = await accounts.UpdateAccountAsync(
                id, request.Name, request.PrivacyPolicyUrl, request.TermsOfServiceUrl,
                request.ConfirmsDisclosure, request.EnableCityAggregates, ct);
            return account is null ? NotFound() : Ok(ToResponse(account));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static AccountSettingsResponse ToResponse(AccountEntity a)
        => new(a.Id, a.Name, a.PrivacyPolicyUrl, a.TermsOfServiceUrl, a.EnableCityAggregates);
}
