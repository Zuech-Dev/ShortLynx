using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A user-defined label for grouping/filtering/searching links. Account-scoped; unlike
/// <see cref="CampaignEntity"/> a link may carry any number of tags (see <see cref="LinkTagEntity"/>),
/// and a tag never affects redirect behavior — it's pure organization.
/// </summary>
[Table("Tags")]
public class TagEntity
{
    public Guid Id { get; set; }

    /// <summary>The owning account. Tags scope by AccountId; name is unique within an account.</summary>
    public Guid AccountId { get; set; }

    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
}
