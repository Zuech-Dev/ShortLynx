using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShortLynx.Services.Redirect;

namespace ShortLynx.Web.Pages;

/// <summary>
/// Mode 2 tracking disclosure interstitial (TRACKING_DISCLOSURE_PLAN). GET renders the choice;
/// POST records it in a 30-day preference cookie and sends the visitor back through /{code}, where
/// the redirect handler — the single place that enqueues visits — honours it. Cancel goes to the
/// ShortLynx site with no cookie and nothing recorded, so a recipient who declines once can still
/// choose differently next time.
/// </summary>
public class DisclosureModel(IRedirectService redirectSvc, IConfiguration configuration) : PageModel
{
    public string Code { get; private set; } = string.Empty;
    public string OperatorName { get; private set; } = "the sender";
    public string DestinationHost { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken ct)
    {
        var entry = await redirectSvc.LookupAsync(code, Request.Host.Host, ct);
        if (entry is null) return NotFound();

        // Nothing to disclose (Mode 1, or the operator has a privacy policy) — go straight through.
        if (entry is not { DisclosureRequired: true, UserLinkCodeId: not null })
            return Redirect($"/{Uri.EscapeDataString(code)}");

        Code = code;
        OperatorName = string.IsNullOrWhiteSpace(entry.OperatorName) ? "the sender" : entry.OperatorName;
        // Hostname only, never the full URL: a legitimate-looking interstitial must not be usable
        // to dress up a phishing destination's path or query.
        DestinationHost = Uri.TryCreate(entry.OriginalUrl, UriKind.Absolute, out var uri) ? uri.Host : "its destination";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string code, string choice, CancellationToken ct)
    {
        if (choice is not ("allow" or "anon" or "cancel")) return BadRequest();

        var entry = await redirectSvc.LookupAsync(code, Request.Host.Host, ct);
        if (entry is null) return NotFound();

        if (choice == "cancel")
        {
            // No cookie, no visit event, destination never contacted.
            var home = configuration["Disclosure:CancelRedirectUrl"] ?? "https://shrtlynx.com";
            return Redirect(home);
        }

        Response.Cookies.Append($"sl_pref_{entry.AccountId}", choice, new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(30),
            Secure = true,
            HttpOnly = true, // read server-side by the redirect handler; nothing on the client needs it
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

        return Redirect($"/{Uri.EscapeDataString(code)}");
    }
}
