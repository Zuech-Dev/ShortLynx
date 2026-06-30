namespace ShortLynx.Core.Models.Responses;

/// <summary>A campaign in the current account, with how many links are currently assigned to it.</summary>
public sealed record CampaignResponse(
    Guid Id,
    string Name,
    string? Description,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    int LinkCount,
    DateTimeOffset CreatedAt);
