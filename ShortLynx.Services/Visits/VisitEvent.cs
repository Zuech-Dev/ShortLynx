namespace ShortLynx.Services.Visits;

public sealed record VisitEvent(
    Guid? ShortCodeId,
    Guid? UserLinkCodeId,
    Guid? UserId,
    string RawIp,
    string? Referrer,
    string? UserAgent,
    DateTimeOffset ClickedAt);
