using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages;

public class IndexModel : PageModel
{
    public sealed record Tier(string Name, string Price, string Period, string[] Points, bool Featured = false);
    public sealed record ComparisonRow(string Feature, string Us, string Bitly, string TinyUrl);

    public (string Title, string Body)[] Privacy { get; } =
    [
        ("IPs are never stored raw", "Only a keyed one-way hash with an hourly-rotating key — clicks can't be linked across time."),
        ("DNT & GPC honored", "Do Not Track and Global Privacy Control are respected: the click still counts, every dimension is dropped."),
        ("k-anonymity (k=10)", "Any breakdown value seen fewer than 10 times folds into “Other” — in the dashboard, the API, and exports."),
        ("No sub-country geography", "Country and timezone only. Never region, city, or coordinates — that's fingerprinting in low-traffic contexts."),
        ("Aggregate-only exports", "Never a row-per-click list that could deanonymize a small campaign. Exports mirror the dashboard."),
        ("Enforced disclosure", "Send tracked links without a privacy policy and recipients see a disclosure screen — and can opt out."),
    ];

    public (string Title, string Body)[] Features { get; } =
    [
        ("Per-recipient attribution", "Mint a unique code per contact so you can see exactly who clicked — no CRM, no login at click time."),
        ("Custom vanity codes", "Choose your own memorable short code (shrtlynx.com/c/your-code) instead of a random one."),
        ("Custom domains", "Bring your own domain, DNS-verified, with per-link domain pinning."),
        ("Campaigns & UTM", "Group links for roll-up reporting with a shared UTM template applied at redirect time."),
        ("Social publishing", "Post a link to Bluesky, Mastodon, Threads, or Reddit with exact per-post click attribution."),
        ("QR codes", "Generate a PNG or SVG QR code for any link or recipient code."),
        ("Privacy-preserving analytics", "Clicks, unique clickers, sources, devices, timeline, hour-of-day, UTM, country + timezone."),
        ("Full REST API", "Scoped API keys for automation — create links, provision codes, pull analytics."),
        ("Self-hostable", "Source-available under ELv2. Run the whole product on your own infrastructure, free."),
    ];

    public Tier[] Tiers { get; } =
    [
        new("Free",    "$0",  "/forever",  ["25 links", "10k redirects/mo", "30-day retention", "Aggregate analytics"]),
        new("Starter", "$9",  "/mo",       ["500 links", "100k redirects/mo", "1-year retention", "1 custom domain", "10 custom codes"]),
        new("Pro",     "$24", "/mo",       ["5,000 links", "1M redirects/mo", "Unlimited retention", "5 custom domains", "Per-recipient attribution"], Featured: true),
        new("Teams",   "$79", "/mo",       ["25,000 links", "10M redirects/mo", "Unlimited retention", "15 custom domains", "Team seats & roles"]),
    ];

    public ComparisonRow[] Comparison { get; } =
    [
        new("Per-recipient click attribution", "Yes", "No", "No"),
        new("Privacy-first (hashed IPs, DNT/GPC)", "Yes", "No", "Limited"),
        new("Self-hostable / open source", "Yes (ELv2)", "No", "No"),
        new("Custom domains", "Yes", "Paid", "Paid"),
        new("Custom vanity codes", "Yes", "Paid", "Yes"),
        new("Free tier", "Generous", "Limited", "Yes"),
    ];

    public (string Q, string A)[] Faq { get; } =
    [
        ("Is ShortLynx a Bitly or TinyURL alternative?",
         "Yes. ShortLynx does everything you'd expect from a URL shortener — custom domains, branded/vanity links, QR codes, click analytics — plus per-recipient attribution and a privacy-first design neither Bitly nor TinyURL offers, and you can self-host it for free."),
        ("What is per-recipient attribution?",
         "A unique short code minted per contact per destination. When someone clicks, you see that specific recipient clicked — ideal for email and sales outreach — without requiring them to log in and without a CRM."),
        ("How is ShortLynx privacy-first?",
         "IP addresses are never stored raw (only an hourly-rotating keyed hash), Do Not Track and Global Privacy Control are honored, breakdowns use k-anonymity, no sub-country geography is stored, and exports are aggregate-only."),
        ("Can I self-host ShortLynx?",
         "Yes — it's source-available under the Elastic License 2.0. Self-hosting is free and unrestricted at every tier, on your own infrastructure and database."),
        ("When is the hosted service available?",
         "The hosted service is coming soon. In the meantime you can run the full product yourself today."),
    ];

    /// <summary>Absolute URL of this page, for the canonical link and OpenGraph/JSON-LD tags.</summary>
    public string Canonical { get; private set; } = "https://shrtlynx.com/";

    public void OnGet()
    {
        Canonical = $"{Request.Scheme}://{Request.Host}/";
    }
}
