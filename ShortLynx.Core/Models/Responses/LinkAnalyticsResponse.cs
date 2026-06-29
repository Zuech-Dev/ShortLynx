namespace ShortLynx.Core.Models.Responses;

public sealed record CodeClickStats(
    string Code,
    Guid? UserId,
    long ClickCount);

public sealed record LinkAnalyticsResponse(
    Guid LinkId,
    string Url,
    string Mode,
    long TotalClicks,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<CodeClickStats> Codes);
