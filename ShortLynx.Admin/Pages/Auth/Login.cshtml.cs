using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Admin.Pages.Auth;

[AllowAnonymous]
public class LoginModel(IMagicLinkService magicLinkService, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await magicLinkService.CreateTokenAsync(Email, ct);
        }
        catch (Exception ex)
        {
            // Don't surface the raw exception (it can leak provider/config details). Log server-side,
            // show a generic message.
            logger.LogError(ex, "Failed to send sign-in email");
            ModelState.AddModelError(string.Empty, "Couldn't send the sign-in email — please try again.");
            return Page();
        }

        Sent = true;
        return Page();
    }
}
