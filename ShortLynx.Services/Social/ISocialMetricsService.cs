namespace ShortLynx.Services.Social;

/// <summary>Settings for the scheduled social-metrics refresh. Bound from the "SocialMetrics" section.</summary>
public sealed class SocialMetricsOptions
{
    /// <summary>How often the background pass runs, and how stale a post's metrics may get before it's re-pulled.</summary>
    public int RefreshIntervalMinutes { get; set; } = 60;

    /// <summary>Posts older than this stop being refreshed — engagement flattens; no point polling forever.</summary>
    public int RefreshWindowDays { get; set; } = 14;
}

/// <summary>
/// Pulls current engagement metrics (likes/reposts/replies; impressions where a platform provides them)
/// for published posts and stores them on the <c>SocialPost</c> rows, beside our click data.
/// </summary>
public interface ISocialMetricsService
{
    /// <summary>Refreshes all of a link's posts now (manual pull). Returns how many were updated.</summary>
    Task<int> RefreshLinkAsync(Guid accountId, Guid linkId, CancellationToken ct = default);

    /// <summary>
    /// Scheduled pass: refreshes posts inside the window whose metrics are missing or stale. One dead
    /// connection or deleted post never blocks the rest. Returns how many were updated.
    /// </summary>
    Task<int> RefreshRecentAsync(CancellationToken ct = default);
}
