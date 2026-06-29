namespace ShortLynx.Core.Options;

/// <summary>
/// Link presentation settings for the API. Bound from the "Links" configuration section.
/// </summary>
public sealed class LinkUrlOptions
{
    /// <summary>
    /// Public base URL of the redirect site (e.g. <c>https://shrtlynx.com</c>), used to build the full
    /// short URL a QR code encodes. Empty ⇒ the bare code is encoded (set this in any real deployment).
    /// A link pinned to a verified custom domain uses that domain instead.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
