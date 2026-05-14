using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

public class LinkEntity
{
    // Primary Key, ValueGeneratedNever
    public Guid Id { get; set; }
    // TODO: Resolve warning
    public required string OriginalUrl { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    // FK -> ApiKey
    public Guid ApiKeyId { get; set; }
    public LinkMode Mode { get; set; }
    public virtual ApiKeyEntity ApiKey { get; set; } = null!;
}
