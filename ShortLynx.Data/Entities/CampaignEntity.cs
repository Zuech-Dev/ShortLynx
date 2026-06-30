using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// Groups links for a marketing push so their clicks roll up into one report. Account-scoped, like
/// links and domains. The optional UTM fields form a default template appended to the destinations of
/// the campaign's links (applied at redirect time) so downstream tools (GA, etc.) attribute the traffic.
/// </summary>
[Table("Campaigns")]
public class CampaignEntity
{
    public Guid Id { get; set; }

    /// <summary>The owning account. Campaigns scope by AccountId.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Audit only: the user who created the campaign.</summary>
    public Guid? UserAccountId { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }

    // Optional default utm_* template applied to the destinations of this campaign's links.
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual UserAccountEntity? UserAccount { get; set; }
    public ICollection<LinkEntity> Links { get; set; } = [];
}
