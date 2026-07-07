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
    // Mode 2 recipient engagement: provisioned codes across the campaign's user-attributed links,
    // how many were clicked at least once, and how fast (minutes from code creation to first click;
    // percentiles are null until a code has been clicked). Zeroes when the campaign has no Mode 2 links.
    int RecipientsTotal,
    int RecipientsClicked,
    double? MedianTimeToFirstClickMinutes,
    double? P90TimeToFirstClickMinutes,
    IReadOnlyList<CampaignLinkClicks> Links);
