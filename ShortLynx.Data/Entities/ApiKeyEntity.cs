using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

[Table("ApiKeys")]
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

    /// <summary>The account this key acts on behalf of (owns its links). Resources scope by AccountId.</summary>
    public Guid AccountId { get; set; }
    /// <summary>Audit only: the user who created the key (if minted from the dashboard).</summary>
    public Guid? UserAccountId { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual ICollection<LinkEntity> Links { get; set; } = [];
    public virtual UserAccountEntity? UserAccount { get; set; }
}
