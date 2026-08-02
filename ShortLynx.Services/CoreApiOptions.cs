namespace ShortLynx.Services;

/// <summary>
/// Where ShortLynx.Core (or ShortLynx.Hosted.Host, which reuses Core's controllers) is reachable from
/// outside — needed by anything that isn't Core itself but must link to one of its endpoints. Currently
/// only Admin's Social page, for the Threads/Reddit "Connect" links: those OAuth authorize/callback
/// routes moved from Admin into Core's <c>SocialOAuthController</c>, so Admin can no longer use a bare
/// relative href for them.
/// </summary>
public sealed class CoreApiOptions
{
    public string PublicBaseUrl { get; set; } = string.Empty;
}
