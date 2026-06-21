namespace ShortLynx.Services.Redirect;

public sealed record RedirectCacheEntry(
    string OriginalUrl,
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,
    // When set, the link is pinned to this host and only resolves when the request's Host matches.
    string? PinnedHost = null);
