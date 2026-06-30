namespace ShortLynx.Services.Campaigns;

/// <summary>
/// Merges a campaign's default utm_* values into a destination URL. Pure and deterministic. Existing
/// query params are preserved and a UTM the destination already carries is never overwritten, so a
/// link that set its own utm_source keeps it. Applied when the redirect target is resolved.
/// </summary>
public static class UtmTemplate
{
    public static string Apply(string url, string? source, string? medium, string? campaign)
    {
        if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(medium) && string.IsNullOrEmpty(campaign))
            return url;

        // Only rewrite absolute http(s) URLs; anything else we pass through untouched.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return url;

        var query = uri.Query.TrimStart('?');
        var existingKeys = ParseKeys(query);

        var additions = new List<string>(3);
        AddIfMissing(additions, existingKeys, "utm_source", source);
        AddIfMissing(additions, existingKeys, "utm_medium", medium);
        AddIfMissing(additions, existingKeys, "utm_campaign", campaign);
        if (additions.Count == 0) return url;

        var merged = string.Join('&', additions);
        var newQuery = query.Length == 0 ? merged : $"{query}&{merged}";

        // Reassemble by hand rather than via UriBuilder, whose Query setter would re-escape our
        // already-encoded values (turning %20 into %2520). GetLeftPart(Path) is everything up to the
        // query; Fragment carries its own leading '#' (or is empty).
        return $"{uri.GetLeftPart(UriPartial.Path)}?{newQuery}{uri.Fragment}";
    }

    private static HashSet<string> ParseKeys(string query)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            keys.Add(eq >= 0 ? pair[..eq] : pair);
        }
        return keys;
    }

    private static void AddIfMissing(List<string> additions, HashSet<string> existingKeys, string key, string? value)
    {
        if (string.IsNullOrEmpty(value) || existingKeys.Contains(key)) return;
        additions.Add($"{key}={Uri.EscapeDataString(value)}");
    }
}
