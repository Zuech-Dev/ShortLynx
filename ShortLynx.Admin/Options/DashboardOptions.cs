namespace ShortLynx.Admin.Options;

/// <summary>
/// Presentation settings for the dashboard. Bound from the "Dashboard" configuration section.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// Public base URL of the redirect site (e.g. <c>https://lynx.example.com</c>), used to render the
    /// full short URL for a code. Empty ⇒ the bare code is shown instead.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Builds the full short URL for a code, or the bare code when no base URL is configured.</summary>
    public string BuildShortUrl(string code) =>
        string.IsNullOrWhiteSpace(PublicBaseUrl) ? code : $"{PublicBaseUrl.TrimEnd('/')}/{code}";
}
