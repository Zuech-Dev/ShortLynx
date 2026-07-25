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

    /// <summary>
    /// Builds the full short URL for a code, or the bare path when no base URL is configured.
    /// <paramref name="isCustom"/> routes the code under <paramref name="customRoutePrefix"/> (the
    /// configured <see cref="ShortLynx.Services.ShortCodes.ShortCodeOptions.CustomRoutePrefix"/>) since
    /// custom codes never resolve at the root path — see <c>ShortUrlBuilder</c>, which this mirrors for
    /// the one call site (a freshly-created link's success banner) too immediate to await a DB round
    /// trip for custom-domain pinning, which a brand-new link can't have yet anyway.
    /// </summary>
    public string BuildShortUrl(string code, bool isCustom, string customRoutePrefix)
    {
        var prefix = customRoutePrefix.Trim('/');
        var path = isCustom && prefix.Length > 0 ? $"{prefix}/{code}" : code;
        return string.IsNullOrWhiteSpace(PublicBaseUrl) ? path : $"{PublicBaseUrl.TrimEnd('/')}/{path}";
    }
}
