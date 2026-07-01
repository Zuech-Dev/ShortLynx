namespace ShortLynx.Services.Analytics;

/// <summary>
/// Reduces an Accept-Language header to its primary language subtag (e.g. "en-US,en;q=0.9" → "en"). The
/// full q-weighted list is high-entropy fingerprinting material; the primary subtag is a useful, coarse
/// signal. Returns null when there's no usable tag.
/// </summary>
public interface ILanguageReducer
{
    string? Primary(string? acceptLanguage);
}

public sealed class LanguageReducer : ILanguageReducer
{
    public string? Primary(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;

        // First list entry, before its q-weight, then the primary subtag before any region.
        var first = acceptLanguage.Split(',')[0].Split(';')[0].Trim();
        if (first.Length == 0 || first == "*") return null;

        var primary = first.Split('-')[0].Trim().ToLowerInvariant();
        return primary.Length is >= 2 and <= 3 && primary.All(char.IsLetter) ? primary : null;
    }
}
