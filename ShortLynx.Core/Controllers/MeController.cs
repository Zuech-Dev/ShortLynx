using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;

namespace ShortLynx.Core.Controllers;

[Route("me")]
public class MeController(
    IAccountService accounts, ShortLynxDbContext db, IUserSessionService sessions,
    IOptions<JwtOptions> jwtOptions) : SessionControllerBase
{
    // GET /me — the current session's user + active account.
    [HttpGet]
    public IActionResult Get() => Ok(new UserSummary(
        CurrentUserId, User.Email(), User.IsAdmin(), AccountId, User.Role()));

    // GET /me/accounts — the accounts the user belongs to (for account switching).
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(CancellationToken ct)
    {
        var list = await accounts.ListAccountsForUserAsync(CurrentUserId, ct);
        return Ok(list.Select(a => new AccountResponse(a.AccountId, a.Name, a.Role.ToString())));
    }

    // POST /me/switch-account — re-issues the session scoped to a different account the user belongs
    // to, so the dashboard can act as it without a fresh sign-in. Membership is re-checked here, not
    // trusted from the list the client displayed, so a stale/tampered client can't switch into an
    // account it was removed from between loading the list and clicking it.
    [HttpPost("switch-account")]
    public async Task<IActionResult> SwitchAccount([FromBody] SwitchAccountRequest request, CancellationToken ct)
    {
        var target = (await accounts.ListAccountsForUserAsync(CurrentUserId, ct))
            .FirstOrDefault(a => a.AccountId == request.AccountId);
        if (target is null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "You're not a member of that account." });

        var user = await db.UserAccountEntities.FirstOrDefaultAsync(u => u.Id == CurrentUserId, ct);
        if (user is null) return Unauthorized();

        var tokens = await sessions.IssueAsync(user, target.AccountId, target.Role, ct);
        SessionCookieWriter.SetSessionCookies(Response, tokens, jwtOptions.Value);

        return Ok(new UserSummary(user.Id, user.Email, user.IsAdmin, target.AccountId, target.Role.ToString()));
    }

    // GET /me/members — members of the current account.
    [HttpGet("members")]
    public async Task<IActionResult> Members(CancellationToken ct)
    {
        var members = await accounts.ListMembersAsync(AccountId, ct);
        return Ok(members.Select(m => new { m.UserAccountId, m.Email, Role = m.Role.ToString(), m.CreatedAt }));
    }

    // POST /me/members — invite a user to the current account at a role. The acting user must be able
    // to manage members and may only grant roles strictly below their own (enforced by the service).
    [HttpPost("members")]
    public async Task<IActionResult> InviteMember([FromBody] InviteMemberRequest request, CancellationToken ct)
    {
        if (!TryParseRole(request.Role, out var role))
            return BadRequest(new { error = $"Unknown role '{request.Role}'." });

        try
        {
            var membership = await accounts.InviteMemberAsync(AccountId, request.Email, role, CurrentUserId, ct);
            return Ok(new { membership.UserAccountId, request.Email, Role = membership.Role.ToString(), membership.CreatedAt });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // PUT /me/members/{userId} — change a member's role (actor must outrank target and may grant the role).
    [HttpPut("members/{userId:guid}")]
    public async Task<IActionResult> ChangeMemberRole(
        Guid userId, [FromBody] ChangeMemberRoleRequest request, CancellationToken ct)
    {
        if (!TryParseRole(request.Role, out var role))
            return BadRequest(new { error = $"Unknown role '{request.Role}'." });

        return await accounts.ChangeRoleAsync(AccountId, userId, role, CurrentUserId, ct)
            ? NoContent()
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "You can't change this member's role." });
    }

    // DELETE /me/members/{userId} — remove a member (actor must outrank the target).
    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid userId, CancellationToken ct)
        => await accounts.RemoveMemberAsync(AccountId, userId, CurrentUserId, ct)
            ? NoContent()
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "You can't remove this member." });

    private static bool TryParseRole(string? value, out AccountRole role)
        => Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}
