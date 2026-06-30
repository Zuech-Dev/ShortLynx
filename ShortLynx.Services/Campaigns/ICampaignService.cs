using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Campaigns;

/// <summary>Mutable fields of a campaign. UTM values are optional and normalised (trimmed, empty ⇒ null).</summary>
public sealed record CampaignInput(
    string Name,
    string? Description = null,
    string? UtmSource = null,
    string? UtmMedium = null,
    string? UtmCampaign = null);

public interface ICampaignService
{
    Task<CampaignEntity> CreateAsync(
        Guid accountId, CampaignInput input, Guid? createdByUserAccountId = null, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignEntity>> ListAsync(Guid accountId, CancellationToken ct = default);

    Task<CampaignEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default);

    Task<CampaignEntity?> UpdateAsync(Guid id, Guid accountId, CampaignInput input, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default);
}
