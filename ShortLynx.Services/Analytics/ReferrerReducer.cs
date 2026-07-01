namespace ShortLynx.Services.Analytics;

/// <summary>
/// Reduces a raw Referer to just its host (path + query dropped, leading <c>www.</c> stripped). The full
/// referrer URL can carry search terms, session tokens, and internal page structure; the host alone keeps
/// the traffic source without that leak. Returns null when there's no usable host.
/// </summary>
public interface IReferrerReducer
{
    string? Host(string? referrer);
}

public sealed class ReferrerReducer : IReferrerReducer
{
    public string? Host(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)) return null;
        if (!Uri.TryCreate(referrer.Trim(), UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return null;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
