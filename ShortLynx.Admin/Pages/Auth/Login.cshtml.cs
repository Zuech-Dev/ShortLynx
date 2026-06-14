using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Admin.Pages.Auth;

[AllowAnonymous]
public class LoginModel(IMagicLinkService magicLinkService) : PageModel
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
            ModelState.AddModelError(string.Empty, $"Could not send sign-in email: {ex.Message}");
            return Page();
        }

        Sent = true;
        return Page();
    }
}
