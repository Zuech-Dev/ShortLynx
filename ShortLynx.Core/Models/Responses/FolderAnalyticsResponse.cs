using ShortLynx.Services.Analytics;

namespace ShortLynx.Core.Models.Responses;

/// <summary>
/// Folder-wide analytics: clicks across every link filed in the folder, same shape as
/// <see cref="CampaignAnalyticsResponse"/> minus the UTM-template concept campaigns have (folders
/// don't apply one). Single-parent join, like campaigns -- a link is filed in at most one folder.
/// </summary>
public sealed record FolderAnalyticsResponse(
    Guid FolderId,
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
