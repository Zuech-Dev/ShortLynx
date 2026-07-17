namespace ShortLynx.Services.Social;

/// <summary>
/// Reddit app credentials, bound from the "Reddit" configuration section. Create the app at
/// reddit.com/prefs/apps (type: "web app"); production API access additionally requires Reddit's
/// data-API approval — see docs/REDDIT_APP_SETUP.md. AppSecret must come from user-secrets/Railway
/// env, never appsettings.json.
/// </summary>
public sealed class RedditOptions
{
    /// <summary>The app's client id (shown under the app name at reddit.com/prefs/apps).</summary>
    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Must exactly match the app's registered redirect uri. Reddit rejects a mismatch.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Reddit REQUIRES a descriptive, unique User-Agent on every API call and blocks default library
    /// values. Format per their rules: "platform:app-id:version (by /u/username)".
    /// </summary>
    public string UserAgent { get; set; } = "web:shortlynx:v1.0 (by /u/shortlynx)";
}
