namespace ShortLynx.Services.Visits;

/// <summary>
/// Capture-time record from the redirect handler. Raw signals (Referrer, UserAgent, AcceptLanguage) are
/// carried through unchanged and reduced to low-entropy buckets in BackgroundVisitWriter.FlushAsync —
/// they are never persisted. When <see cref="PrivacySignal"/> is set (DNT / Sec-GPC), the writer records
/// the click but suppresses all derived dimensions.
/// </summary>
public sealed record VisitEvent(
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,
    // Set when the click came in on a code minted for one social post — recorded on the same Visits
    // row shape as a shared-code click, just attributed to the post instead of guessed from a referrer.
    Guid? SocialPostCodeId,
    string RawIp,
    string? Referrer,
    string? UserAgent,
    DateTimeOffset ClickedAt,
    string? AcceptLanguage = null,
    string? SecFetchSite = null,
    bool PrivacySignal = false,
    // Raw query string of the inbound request; UTM tags are parsed out (and everything else
    // discarded) in the writer, keeping with the derive-at-write-time discipline.
    string? RawQuery = null,
    // Whether the redirect request arrived over HTTPS (after forwarded headers). Browsers send Sec-Fetch-*
    // only to secure origins, so a missing Sec-Fetch-Site means "scanner" only when this is true -- on a
    // plain-HTTP deployment every real click lacks it. Defaults to false so a caller that doesn't know
    // never gets clicks classified as SuspectedAutomated.
    bool SecureRequest = false);
