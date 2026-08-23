using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages;

public class TinyUrlAlternativeModel : PageModel
{
    public sealed record ComparisonRow(string Feature, string Us, string TinyUrl);

    public ComparisonRow[] Comparison { get; } =
    [
        new("Click analytics", "Full breakdown: source, device, browser, OS, country, timeline", "Basic click count"),
        new("Per-recipient click attribution", "Yes, on every tier", "Not offered"),
        new("Self-hosting", "Free, unrestricted, every tier (ELv2)", "Not offered"),
        new("Custom domains", "1 domain free on Starter ($9/mo)", "Paid add-on"),
        new("Custom vanity codes", "Included", "Included"),
        new("IP addresses stored raw", "Never — HMAC-hashed, hourly-rotating", "Not publicly documented"),
        new("QR codes", "PNG & SVG, included", "Included"),
        new("API access", "Included from Pro ($24/mo)", "Paid plans only"),
        new("Source availability", "Source-available (ELv2) — inspect or self-host it", "Closed source"),
    ];

    public (string Q, string A)[] Faq { get; } =
    [
        ("TinyURL is already free — why would I switch?",
         "If all you need is a free, no-account short link, TinyURL is genuinely fine for that. ShortLynx is worth a look once you want more than the link itself: real analytics (not just a count), custom domains, or the ability to see which specific recipient clicked — none of which TinyURL's free tier offers, and self-hosting means you're not paying for them either."),
        ("Does ShortLynx have a no-signup quick-shorten option like TinyURL?",
         "Not today — ShortLynx is built around accounts because analytics, campaigns, and per-recipient attribution all need somewhere to live. If you just need a one-off anonymous link with zero setup, TinyURL's no-account flow is the simpler tool for that specific job."),
        ("Is ShortLynx's free tier actually free, or a trial?",
         "The hosted Free tier (25 links, 10k redirects/month) has no time limit and no credit card required. Self-hosting removes those limits entirely, at no cost, forever — the Elastic License 2.0 only restricts reselling ShortLynx as a competing hosted service."),
        ("What is per-recipient attribution, concretely?",
         "A unique short code minted per contact per destination. Send each recipient their own link, and a click tells you which specific person clicked — not just that the link got clicked. Useful for anything TinyURL's aggregate click count can't answer: email outreach, sales follow-up, per-contact engagement tracking."),
    ];

    public string FaqJsonLd => ShortLynx.Web.Seo.FaqJsonLd.Build(Faq);
}
