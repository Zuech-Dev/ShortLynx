using System.Text.Json.Nodes;

namespace ShortLynx.Web.Seo;

/// <summary>
/// Builds schema.org FAQPage JSON-LD from a page's FAQ list. Built via <see cref="JsonObject"/>
/// rather than string interpolation so question/answer text is always escaped correctly regardless
/// of what characters it contains — a hand-built JSON string is one stray quote away from invalid
/// or, worse, broken-but-still-parses output.
/// </summary>
public static class FaqJsonLd
{
    public static string Build(IEnumerable<(string Q, string A)> faq)
    {
        var mainEntity = new JsonArray();
        foreach (var (q, a) in faq)
        {
            mainEntity.Add(new JsonObject
            {
                ["@type"] = "Question",
                ["name"] = q,
                ["acceptedAnswer"] = new JsonObject
                {
                    ["@type"] = "Answer",
                    ["text"] = a,
                },
            });
        }

        var root = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = mainEntity,
        };

        return root.ToJsonString();
    }
}
