using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShortLynx.Web.Pages.Blog;

public class GdprCompliantUrlShortenerModel : PageModel
{
    public (string Q, string A)[] Faq { get; } =
    [
        ("Does using ShortLynx make my business GDPR compliant?",
         "No single tool can make an organization compliant on its own — compliance depends on your full data flow, your legal basis for processing, your retention practices, and decisions this article can't see. What ShortLynx's redirect pipeline does is reduce what there is to get wrong at the tool level: it doesn't retain raw IPs or full user-agent strings, so there's less personal data in play for you to have to justify, secure, and eventually delete. Whether that's sufficient for your specific use case is a question for whoever handles compliance at your organization, not this page."),
        ("Is a hashed IP address still personal data under GDPR?",
         "Regulatory guidance generally treats a hash as pseudonymized data rather than anonymized data — it's not directly identifying, but if the hashing method could plausibly be reversed or correlated back to an individual by someone with the right access, it's still in scope as personal data, just processed more safely. This is a genuine legal question with jurisdiction-specific nuance, not a settled technical fact — don't treat this paragraph as a legal conclusion for your situation."),
        ("What does k-anonymity actually protect against?",
         "It stops an aggregate report from accidentally re-identifying someone through a small number. If a breakdown would show \"1 click from Iceland,\" that's close to naming a specific person if the audience is small enough — k-anonymity (k=10) folds any value seen fewer than 10 times into an \"Other\" bucket instead, so no row in an export or dashboard can single out an individual."),
        ("Does honoring Do Not Track have anything to do with GDPR specifically?",
         "Not directly — DNT and Global Privacy Control are more closely tied to ePrivacy-style consent signals and US state privacy laws (like CCPA) than to GDPR itself. They're included here because they're the same underlying instinct: if a visitor has signaled they don't want to be tracked, don't collect the categorized data, even though GDPR itself doesn't mandate honoring that specific signal."),
    ];

    public string FaqJsonLd => ShortLynx.Web.Seo.FaqJsonLd.Build(Faq);
}
