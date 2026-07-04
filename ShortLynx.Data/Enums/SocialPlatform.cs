namespace ShortLynx.Data.Enums;

/// <summary>
/// A social platform an account can connect for publishing tracked links and reading post metrics.
/// Tier-A (open APIs) first; gated platforms (Threads, Reddit) are added in a later phase.
/// </summary>
public enum SocialPlatform
{
    Bluesky = 0,
    Mastodon = 1,

    /// <summary>Gated (Meta App Review). Connected via OAuth from the dashboard, not the credentials API.</summary>
    Threads = 2,
}
