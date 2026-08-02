namespace ShortLynx.Services.Social;

/// <summary>
/// Where the browser-redirect OAuth flow (Threads, Reddit) sends the user back to after success or
/// failure, bound from the "SocialOAuth" configuration section. The dashboard's own social-connections
/// page — <c>?connected={platform}</c> or <c>?{platform}Error=...</c> is appended to this.
/// </summary>
public sealed class SocialOAuthOptions
{
    /// <summary>
    /// Absolute URL for a real deployment (e.g. <c>https://shortlynx.dev/social</c>). Defaults to a
    /// bare relative path so an unconfigured deployment fails as a 404 on redirect rather than an
    /// exception — the same "fail visibly, not loudly" shape as the platform-not-configured error.
    /// </summary>
    public string ReturnUrlBase { get; set; } = "/social";
}
