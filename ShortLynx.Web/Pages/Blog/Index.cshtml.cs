using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages.Blog;

public class IndexModel : PageModel
{
    public sealed record Post(string Title, string Url, string Excerpt, string Date);

    // Hand-maintained, matching the actual post pages under Pages/Blog/ — there's no CMS behind
    // this, deliberately: a handful of long-form pages don't justify one, and every post already
    // needs its own .cshtml for page-specific meta/JSON-LD anyway.
    public Post[] Posts { get; } =
    [
        new(
            "Email click tracking without a CRM",
            "/blog/email-click-tracking-without-a-crm",
            "Per-recipient click attribution — how to know which specific contact clicked an email link, without wiring up Outreach, Salesloft, or a CRM at all.",
            "2026-08-22"),
        new(
            "What actually makes a URL shortener GDPR-compliant",
            "/blog/gdpr-compliant-url-shortener",
            "Most shorteners store the raw IP and user-agent of every click. Here's what changes — technically, not just in a privacy policy — when a shortener is actually built not to.",
            "2026-08-22"),
    ];
}
