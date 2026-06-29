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
    public LinkMode Mode { get; set; }

    /// <summary>The owning account. All ownership scoping is by AccountId.</summary>
    public Guid AccountId { get; set; }

    // Provenance only (who/what created the link); ownership is AccountId:
    //  - ApiKeyId set     → created programmatically via the REST API
    //  - UserAccountId set → created by a signed-in user in the admin dashboard
    public Guid? ApiKeyId { get; set; }
    public Guid? UserAccountId { get; set; }

    // Optional: pin this link to a specific verified custom domain. When set, the redirect only
    // resolves the link's codes under that host; when null, the link resolves on any host.
    public Guid? CustomDomainId { get; set; }

    public ICollection<VisitEntity> Visits { get; set; } = [];
    public UserLinkCodeEntity? UserLinkCode { get; set; } = null;
    public virtual AccountEntity Account { get; set; } = null!;
    public virtual ApiKeyEntity? ApiKey { get; set; }
    public virtual UserAccountEntity? UserAccount { get; set; }
    public virtual CustomDomainEntity? CustomDomain { get; set; }
}
