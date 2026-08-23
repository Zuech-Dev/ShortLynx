using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages.Blog;

public class EmailClickTrackingWithoutCrmModel : PageModel
{
    public (string Q, string A)[] Faq { get; } =
    [
        ("Does the recipient have to click a special link, or does this work with any link?",
         "It has to be the per-recipient short link — that's what carries the identity. If you paste the plain destination URL instead, there's nothing to attribute the click to. In practice this means generating one short link per recipient before you send, not after."),
        ("Do I need to tell recipients their click is being tracked?",
         "If you haven't published a privacy policy covering it, ShortLynx shows an interstitial disclosure screen before the redirect completes, and the recipient can decline to be tracked — the destination still loads either way. If you do have your own privacy policy, you're responsible for it covering link tracking; ShortLynx enforces disclosure by default specifically so this isn't something you can accidentally skip."),
        ("Does this work for cold outreach, or only existing contacts?",
         "Technically it works for anyone you have an email address for. Whether it's appropriate is a separate question from whether it works — the same deliverability and consent norms that apply to any outbound email apply here too."),
        ("How is this different from an email open-tracking pixel?",
         "An open-tracking pixel is a 1x1 image that has to load for you to know the email was opened — and Apple Mail Privacy Protection, most corporate proxies, and many mail clients now pre-fetch or block images by default, which makes open rates unreliable in ways that are hard to detect. A click is a much cleaner signal: the recipient took a deliberate action, and nothing about it depends on whether their mail client decided to render an image."),
    ];

    public string FaqJsonLd => ShortLynx.Web.Seo.FaqJsonLd.Build(Faq);
}
