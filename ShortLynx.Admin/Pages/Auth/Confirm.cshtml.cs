using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Context;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Admin.Pages.Auth;

[AllowAnonymous]
public class ConfirmModel(
    IMagicLinkService magicLinkService,
    IDbContextFactory<ShortLynxDbContext> dbFactory,
    IAccountService accountService,
    IOptions<AccessControlOptions> adminOptions) : PageModel
{
    public string Error { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] string? token,
        [FromQuery] string? returnUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            Error = "No token was provided in the link.";
            return Page();
        }

        var user = await magicLinkService.ValidateTokenAsync(token, ct);
        if (user is null)
        {
            Error = "This link is invalid, has already been used, or has expired. Request a new one.";
            return Page();
        }

        // Authorization gate: a user may sign in if they're on the allowlist (bootstrap owners /
        // platform super-admins) OR they belong to at least one account (invited members). A valid
        // token alone is never sufficient.
        var opts = adminOptions.Value;
        var accounts = await accountService.ListAccountsForUserAsync(user.Id, ct);
        if (!opts.IsAllowed(user.Email) && accounts.Count == 0)
        {
            Error = "This account is not authorised to access the admin dashboard.";
            return Page();
        }

        var isSuperAdmin = opts.IsSuperAdmin(user.Email);

        // Config is the source of truth for admin status at sign-in; keep the persisted flag in sync.
        if (user.IsAdmin != isSuperAdmin)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.UserAccountEntities
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, isSuperAdmin), ct);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
        };
        if (isSuperAdmin)
            claims.Add(new Claim(AdminClaims.IsAdmin, "true"));

        // Stamp the current account + role (the highest-role membership) when the user has one.
        // Allowlisted users without a membership get a personal account lazily on first page load.
        if (accounts.Count > 0)
        {
            var primary = accounts[0];
            claims.Add(new Claim(AdminClaims.AccountId, primary.AccountId.ToString()));
            claims.Add(new Claim(AdminClaims.AccountRole, primary.Role.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        var safe = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return LocalRedirect(safe);
    }
}
