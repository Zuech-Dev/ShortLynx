namespace ShortLynx.Core.Models.Responses;

/// <summary>A connected social account. Tokens are never included — they exist only encrypted at rest.</summary>
public sealed record SocialConnectionResponse(
    Guid Id,
    string Platform,
    string Handle,
    string? InstanceUrl,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>Per-connection outcome of a publish request (partial success is expected and normal).</summary>
public sealed record PublishTargetResponse(
    Guid ConnectionId,
    string Handle,
    bool Success,
    string? PostUrl,
    string? Error);

/// <summary>A post published for a link. Metrics are null until the first metrics pull.</summary>
public sealed record SocialPostResponse(
    Guid Id,
    string Platform,
    string Handle,
    string? PostUrl,
    string Text,
    DateTimeOffset PostedAt,
    long? Impressions,
    long? Likes,
    long? Reposts,
    long? Replies,
    DateTimeOffset? MetricsUpdatedAt,
    // Clicks traced to THIS post via its own short code — exact, not inferred from a referrer, and
    // therefore able to separate two posts on the same platform. Unique dedupes within an hour (the IP
    // hash rotates hourly by design), so compare posts on Clicks rather than reading Unique as reach.
    long Clicks = 0,
    long UniqueClicks = 0);
