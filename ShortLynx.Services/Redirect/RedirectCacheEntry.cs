namespace ShortLynx.Services.Redirect;

public sealed record RedirectCacheEntry(
    string OriginalUrl,
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,
    // Set when the code resolved to one social post's own code (see SocialPostCodeEntity).
    Guid? SocialPostCodeId,
    // When set, the link is pinned to this host and only resolves when the request's Host matches.
    string? PinnedHost = null,
    // Mode 2 disclosure (TRACKING_DISCLOSURE_PLAN): when the operator has no privacy policy URL,
    // the redirect pauses on the ShortLynx interstitial. AccountId scopes the preference cookie;
    // OperatorName is shown on the interstitial.
    bool DisclosureRequired = false,
    Guid? AccountId = null,
    string? OperatorName = null,
    // One-time codes are claimed by the handler *after* any disclosure choice, never during lookup,
    // so rendering the interstitial can't burn the code.
    bool IsOneTimeUse = false);
