using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages;

public class BitlyAlternativeModel : PageModel
{
    public sealed record ComparisonRow(string Feature, string Us, string Bitly);

    public ComparisonRow[] Comparison { get; } =
    [
        new("Per-recipient click attribution", "Yes, on every tier", "Not offered at any tier"),
        new("Self-hosting", "Free, unrestricted, every tier (ELv2)", "Not offered"),
        new("Free tier custom domains", "1 domain, Starter tier ($9/mo)", "Not on the free tier"),
        new("Branded/vanity short codes", "Included", "Paid tiers only"),
        new("IP addresses stored raw", "Never — HMAC-hashed, hourly-rotating", "Not publicly documented"),
        new("QR codes", "PNG & SVG, included", "Included, watermarked below Pro"),
        new("API access", "Included from Pro ($24/mo)", "Enterprise plans only"),
        new("Data export", "Aggregate-only, every tier", "Varies by plan"),
        new("Pricing model", "Free self-host, or $0–$79/mo hosted", "$8–$300+/mo, seat-based"),
        new("Link limit on paid tiers", "500–25,000/mo by tier", "Often capped lower per seat"),
    ];

    public (string Q, string A)[] Faq { get; } =
    [
        ("Is ShortLynx really free, or is that a limited trial?",
         "Self-hosting is free forever, at every tier, with no feature gate — the Elastic License 2.0 only restricts reselling ShortLynx as a competing hosted service, not using it. If you'd rather not run your own server, the hosted plans start at $0/month (Free tier) with paid tiers for higher volume."),
        ("What does Bitly do that ShortLynx doesn't?",
         "Bitly has a longer track record, a mobile app, and deeper integrations with some third-party marketing platforms. If those specific integrations matter more to you than per-recipient attribution or self-hosting, Bitly may still be the better fit — this isn't a claim that ShortLynx wins on every axis, only the ones that differ from most \"alternative\" pages: privacy, attribution, and cost."),
        ("Can I migrate my existing Bitly links to ShortLynx?",
         "There's no automated import tool today — Bitly doesn't offer a public bulk-export of your full link history in a format built for migration. You can recreate your active links in ShortLynx and update where they're shared; existing bit.ly links keep working independently, so there's no rush or risk in switching gradually."),
        ("Does switching away from Bitly break my old links?",
         "No. Your existing bit.ly short links keep resolving exactly as before — switching only affects links you create going forward. There's nothing to migrate or break."),
        ("Is per-recipient attribution the same as Bitly's click analytics?",
         "No — Bitly (like most shorteners) reports aggregate click counts per link: \"this link got 400 clicks.\" ShortLynx's Mode 2 links mint a distinct short code per recipient, so a click tells you which specific contact clicked, not just that someone did. It's the difference between a page-view counter and a CRM-grade attribution log, without needing the CRM."),
    ];

    public string FaqJsonLd => ShortLynx.Web.Seo.FaqJsonLd.Build(Faq);
}
