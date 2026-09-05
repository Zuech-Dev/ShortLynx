using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A pure organizational collection of links. Account-scoped, structurally like
/// <see cref="CampaignEntity"/> (one folder per link, via <see cref="LinkEntity.FolderId"/>) but
/// deliberately without a UTM template or redirect-time behavior — a folder never changes what a
/// link does, only how it's browsed. Campaigns stay the marketing-rollup construct; folders are just
/// filing.
/// </summary>
[Table("Folders")]
public class FolderEntity
{
    public Guid Id { get; set; }

    /// <summary>The owning account. Folders scope by AccountId.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Audit only: the user who created the folder.</summary>
    public Guid? UserAccountId { get; set; }

    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual UserAccountEntity? UserAccount { get; set; }
    public ICollection<LinkEntity> Links { get; set; } = [];
}
