using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Admin.Pages.Auth;

public class LogoutModel : PageModel
{
    // POST-only + antiforgery so a forged GET (CSRF/link prefetch) can't sign the user out.
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Auth/Login");
    }
}
