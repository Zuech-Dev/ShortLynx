using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Campaigns;

public sealed class CampaignService(ShortLynxDbContext db) : ICampaignService
{
    public async Task<CampaignEntity> CreateAsync(
        Guid accountId, CampaignInput input, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a campaign name.", nameof(input));

        var entity = new CampaignEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            UserAccountId = createdByUserAccountId,
            Name = name,
            Description = Clean(input.Description),
            UtmSource = Clean(input.UtmSource),
            UtmMedium = Clean(input.UtmMedium),
            UtmCampaign = Clean(input.UtmCampaign),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.CampaignEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<CampaignEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
        => await db.CampaignEntities
            .Where(c => c.AccountId == accountId)
            .OrderByDescending(c => c.Id)
            .ToListAsync(ct);

    public Task<CampaignEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default)
        => db.CampaignEntities.FirstOrDefaultAsync(c => c.Id == id && c.AccountId == accountId, ct);

    public async Task<CampaignEntity?> UpdateAsync(
        Guid id, Guid accountId, CampaignInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a campaign name.", nameof(input));

        var campaign = await db.CampaignEntities.FirstOrDefaultAsync(c => c.Id == id && c.AccountId == accountId, ct);
        if (campaign is null) return null;

        campaign.Name = name;
        campaign.Description = Clean(input.Description);
        campaign.UtmSource = Clean(input.UtmSource);
        campaign.UtmMedium = Clean(input.UtmMedium);
        campaign.UtmCampaign = Clean(input.UtmCampaign);
        await db.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        // Links keep existing; the FK is configured SetNull, but we don't rely on cascade timing here —
        // unassign first so the operation is provider-agnostic, then delete the campaign.
        await db.LinkEntities
            .Where(l => l.CampaignId == id && l.AccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.CampaignId, (Guid?)null), ct);

        var affected = await db.CampaignEntities
            .Where(c => c.Id == id && c.AccountId == accountId)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }

    // Trims and collapses empty/whitespace to null so optional fields store cleanly.
    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
