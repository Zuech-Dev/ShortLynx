using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Core.Controllers;

[Route("me")]
public class MeController(IAccountService accounts) : SessionControllerBase
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

    // GET /me/members — members of the current account.
    [HttpGet("members")]
    public async Task<IActionResult> Members(CancellationToken ct)
    {
        var members = await accounts.ListMembersAsync(AccountId, ct);
        return Ok(members.Select(m => new { m.UserAccountId, m.Email, Role = m.Role.ToString(), m.CreatedAt }));
    }
}
