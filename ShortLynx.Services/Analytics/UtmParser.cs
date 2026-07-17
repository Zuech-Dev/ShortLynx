namespace ShortLynx.Services.Analytics;

/// <summary>The five standard UTM tags parsed off the inbound short-link query string. All null when
/// the link carried no tags (or the visitor sent a privacy signal — suppressed like every dimension).</summary>
public sealed record UtmTags(
    string? Source = null,
    string? Medium = null,
    string? Campaign = null,
    string? Term = null,
    string? Content = null)
{
    public static readonly UtmTags Empty = new();
}

/// <summary>
/// Pure parser for UTM parameters on the *inbound* request (the short link itself carries them, e.g.
/// <c>/abc123?utm_source=newsletter</c>) — not the destination URL. Keys are case-insensitive; values
/// are trimmed, URL-decoded, and truncated so an attacker can't stuff arbitrary payloads into the
/// analytics store. Kept free of ASP.NET types so it stays testable and usable from the writer.
/// </summary>
public static class UtmParser
{
    // Long enough for real campaign names, short enough to bound storage per click.
    private const int MaxValueLength = 100;

    public static UtmTags Parse(string? query)
    {
        if (string.IsNullOrEmpty(query)) return UtmTags.Empty;

        string? source = null, medium = null, campaign = null, term = null, content = null;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            var key = pair[..eq];
            if (!key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;

            var value = Decode(pair[(eq + 1)..]);
            if (string.IsNullOrEmpty(value)) continue;

            // First occurrence wins, matching typical analytics-tool behaviour.
            switch (key.ToLowerInvariant())
            {
                case "utm_source": source ??= value; break;
                case "utm_medium": medium ??= value; break;
                case "utm_campaign": campaign ??= value; break;
                case "utm_term": term ??= value; break;
                case "utm_content": content ??= value; break;
            }
        }

        return source is null && medium is null && campaign is null && term is null && content is null
            ? UtmTags.Empty
            : new UtmTags(source, medium, campaign, term, content);
    }

    private static string? Decode(string raw)
    {
        string value;
        try
        {
            value = Uri.UnescapeDataString(raw.Replace('+', ' ')).Trim();
        }
        catch (UriFormatException)
        {
            return null; // malformed escaping — drop rather than store garbage
        }

        if (value.Length == 0) return null;
        return value.Length <= MaxValueLength ? value : value[..MaxValueLength];
    }
}
