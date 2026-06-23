using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Users;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Platform user management for super-admins (is_admin). Cross-tenant: add/edit/deactivate users and
/// assign them to existing accounts at a role. Account-scoped membership management lives under
/// <c>/me/members</c>.
/// </summary>
[ApiController]
[Route("admin/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = AuthorizationPolicies.SuperAdmin)]
public class AdminUsersController(IUserAdminService users) : ControllerBase
{
    // GET /admin/users?page=&pageSize=
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var list = await users.ListUsersAsync(page, pageSize, ct);
        return Ok(list.Select(ToResponse));
    }

    // POST /admin/users — add a user, optionally assigned to an existing account at a role.
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AdminAddUserRequest request, CancellationToken ct)
    {
        AccountRole? role = null;
        if (request.Role is not null)
        {
            if (!TryParseRole(request.Role, out var parsed))
                return BadRequest(new { error = $"Unknown role '{request.Role}'." });
            role = parsed;
        }

        try
        {
            var view = await users.AddUserAsync(request.Email, request.AccountId, role, request.AccountName, ct);
            return Ok(ToResponse(view));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // PUT /admin/users/{id} — toggle active and/or super-admin (soft-delete = IsActive:false).
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] AdminEditUserRequest request, CancellationToken ct)
    {
        var found = false;
        if (request.IsActive is { } active)
            found |= await users.SetActiveAsync(id, active, ct);
        if (request.IsAdmin is { } admin)
            found |= await users.SetSuperAdminAsync(id, admin, ct);

        if (request.IsActive is null && request.IsAdmin is null)
            return BadRequest(new { error = "Provide isActive and/or isAdmin." });

        return found ? NoContent() : NotFound();
    }

    // DELETE /admin/users/{id} — soft delete (deactivate): blocks sign-in, keeps the record.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
        => await users.SetActiveAsync(id, false, ct) ? NoContent() : NotFound();

    // PUT /admin/users/{id}/accounts/{accountId} — assign to an existing account or change the role.
    [HttpPut("{id:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> AssignAccount(
        Guid id, Guid accountId, [FromBody] AdminAssignAccountRequest request, CancellationToken ct)
    {
        if (!TryParseRole(request.Role, out var role))
            return BadRequest(new { error = $"Unknown role '{request.Role}'." });

        try
        {
            return await users.AssignToAccountAsync(id, accountId, role, ct) ? NoContent() : NotFound();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    // DELETE /admin/users/{id}/accounts/{accountId} — remove the user's membership in an account.
    [HttpDelete("{id:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> RemoveFromAccount(Guid id, Guid accountId, CancellationToken ct)
        => await users.RemoveFromAccountAsync(id, accountId, ct) ? NoContent() : NotFound();

    private static AdminUserResponse ToResponse(AdminUserView u) => new(
        u.Id, u.Email, u.IsActive, u.IsAdmin, u.CreatedAt,
        u.Accounts.Select(a => new AccountResponse(a.AccountId, a.Name, a.Role.ToString())).ToArray());

    private static bool TryParseRole(string? value, out AccountRole role)
        => Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}
