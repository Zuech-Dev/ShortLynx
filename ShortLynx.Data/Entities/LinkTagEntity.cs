using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// Joins a <see cref="LinkEntity"/> to a <see cref="TagEntity"/> (many-to-many). An explicit join
/// entity with its own PK, matching this codebase's convention for many-to-many relationships (see
/// <see cref="MembershipEntity"/>) rather than EF's implicit skip-navigation many-to-many.
/// </summary>
[Table("LinkTags")]
public class LinkTagEntity
{
    public Guid Id { get; set; }
    public Guid LinkId { get; set; }
    public Guid TagId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public virtual LinkEntity Link { get; set; } = null!;
    public virtual TagEntity Tag { get; set; } = null!;
}
