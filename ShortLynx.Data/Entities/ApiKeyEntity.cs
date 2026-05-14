namespace ShortLynx.Data.Entities;

public class ApiKeyEntity
{
    public Guid Id { get; set; }
    public string Prefix { get; set; }
    public string KeyHash { get; set; }
    public string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    // TODO: Parse in the service layer
    public string Scopes { get; set; }
    public virtual ICollection<LinkEntity> Links { get; set; } = [];
}
