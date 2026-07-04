namespace ShortLynx.Services.Social;

/// <summary>
/// The Threads-specific app credentials, bound from the "Threads" configuration section. Meta's app
/// dashboard provisions a credential pair dedicated to the Threads product (App Settings → Basic →
/// "Threads app ID"/"Threads app secret"), separate from the app-level App ID/Secret used by the
/// general Meta/Facebook Graph API — a future Facebook/Instagram connector would use that app-level
/// pair (its own "Meta"/"Facebook" options), not this one. AppSecret must come from user-secrets/Railway
/// env, never appsettings.json. Needed by every service that runs <see cref="ThreadsConnector"/> — the
/// OAuth code exchange only happens where the callback endpoint lives (Admin), but publish/metrics calls
/// (which can originate from Core's API too) still sign requests with <c>appsecret_proof</c>, so both
/// apps need AppSecret configured.
/// </summary>
public sealed class ThreadsOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Must exactly match the "Redirect Callback URL" configured in the Meta app dashboard, e.g.
    /// <c>https://shortlynx.dev/social/threads/callback</c>. Meta rejects a mismatch outright.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Graph API version segment used in Threads API URLs, e.g. "v21.0".</summary>
    public string ApiVersion { get; set; } = "v21.0";
}
