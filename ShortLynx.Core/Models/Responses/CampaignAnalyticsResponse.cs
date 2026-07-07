using ShortLynx.Services.Analytics;

namespace ShortLynx.Core.Models.Responses;

/// <summary>Per-link totals within a campaign roll-up.</summary>
public sealed record CampaignLinkClicks(
    Guid LinkId,
    string Url,
    string Mode,
    long TotalClicks,
    long UniqueClicks);

/// <summary>
/// Campaign-wide analytics: clicks across every link in the campaign (both anonymous and
/// user-attributed), with the same source/device/timeline breakdowns as link analytics plus a
/// per-link table. <c>UniqueClicks</c> carries the same per-hour caveat as link analytics.
/// </summary>
public sealed record CampaignAnalyticsResponse(
    Guid CampaignId,
    string Name,
    int LinkCount,
    long TotalClicks,
    long UniqueClicks,
    // Human/bot split as on LinkAnalyticsResponse: HumanClicks + BotClicks = TotalClicks.
    long HumanClicks,
    long HumanUniqueClicks,
    long BotClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline,
    // All 24 UTC hour-of-day buckets (zero-filled), for send-window analysis.
    IReadOnlyList<HourlyClicks> HourlyDistribution,
    IReadOnlyList<CampaignLinkClicks> Links);
