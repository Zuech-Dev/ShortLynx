using ShortLynx.Services.Analytics;

namespace ShortLynx.Core.Models.Responses;

/// <summary>
/// Tag-wide analytics: clicks across every link carrying the tag, same shape as
/// <see cref="FolderAnalyticsResponse"/>. Unlike a folder (single-parent), a link can carry several
/// tags -- it contributes to each tag's rollup independently, which needs no special fan-out logic
/// beyond the per-tag link-id query in <see cref="MeTagsController"/>.
/// </summary>
public sealed record TagAnalyticsResponse(
    Guid TagId,
    string Name,
    int LinkCount,
    long TotalClicks,
    long UniqueClicks,
    long HumanClicks,
    long HumanUniqueClicks,
    long BotClicks,
    DateTimeOffset? FirstClickAt,
    DateTimeOffset? LastClickAt,
    IReadOnlyList<SourceCount> Sources,
    IReadOnlyList<DeviceCount> Devices,
    IReadOnlyList<DailyClicks> Timeline,
    IReadOnlyList<HourlyClicks> HourlyDistribution,
    int RecipientsTotal,
    int RecipientsClicked,
    double? MedianTimeToFirstClickMinutes,
    double? P90TimeToFirstClickMinutes,
    IReadOnlyList<CampaignLinkClicks> Links,
    IReadOnlyList<CityCount> Cities,
    IReadOnlyList<LabelCount> Browsers,
    IReadOnlyList<LabelCount> OperatingSystems,
    IReadOnlyList<LabelCount> Languages,
    IReadOnlyList<LabelCount> Countries,
    IReadOnlyList<LabelCount> NavigationTypes,
    IReadOnlyList<LabelCount> UtmSources,
    IReadOnlyList<LabelCount> UtmMediums,
    IReadOnlyList<LabelCount> UtmCampaigns);
