using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("Links")]
public class LinkEntity
{
    public Guid Id { get; set; }

    // TODO: Resolve warning
    public required string OriginalUrl { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public Guid ApiKeyId { get; set; }
    public LinkMode Mode { get; set; }
    public ICollection<VisitEntity> Visits { get; set; } = [];
    public UserLinkCodeEntity? UserLinkCode { get; set; } = null;
    public virtual ApiKeyEntity ApiKey { get; set; } = null!;
}
