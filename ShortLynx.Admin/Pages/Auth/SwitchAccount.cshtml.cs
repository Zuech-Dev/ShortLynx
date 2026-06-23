using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShortLynx.Admin.Extensions;
using ShortLynx.Admin.Options;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Admin.Pages.Auth;

// Changes the account the signed-in user is currently acting in by re-issuing the auth cookie with new
// account_id / account_role claims. POST-only + antiforgery (Razor Pages validates it automatically),
// so a forged GET can't move a user between tenants. Membership is re-checked server-side: a selection
// the user isn't a member of is rejected, so a tampered form value can't reach another tenant's data.
[Authorize]
public class SwitchAccountModel(IAccountService accountService) : PageModel
{
    public async Task<IActionResult> OnPostAsync(
        [FromForm] Guid accountId, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (userId is null)
            return RedirectToPage("/Auth/Login");

        var role = await accountService.GetRoleAsync(accountId, userId.Value, ct);
        if (role is null)
            // Not a member of the requested account — ignore and return to where they were.
            return LocalRedirectOrHome(returnUrl);

        // Preserve every existing claim except the account scope, which we replace.
        var claims = User.Claims
            .Where(c => c.Type is not (AdminClaims.AccountId or AdminClaims.AccountRole))
            .ToList();
        claims.Add(new Claim(AdminClaims.AccountId, accountId.ToString()));
        claims.Add(new Claim(AdminClaims.AccountRole, role.Value.ToString()));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return LocalRedirectOrHome(returnUrl);
    }

    private IActionResult LocalRedirectOrHome(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : LocalRedirect("/");
}
