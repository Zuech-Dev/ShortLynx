namespace ShortLynx.Services.Redirect;

public sealed record RedirectCacheEntry(
    string OriginalUrl,
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId);
