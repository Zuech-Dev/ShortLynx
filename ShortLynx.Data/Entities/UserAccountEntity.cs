using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

[Table("UserAccounts")]
public class UserAccountEntity
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// When the user last completed a sign-in (magic-link exchange in Core, or the Admin confirm page).
    /// Token refreshes don't touch it, so it's "last sign-in", not "last seen". Null = never since this
    /// column existed. Read by platform-admin overviews; never shown to other tenants.
    /// </summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>
    /// Grants access to cross-tenant admin pages (user list, global totals). Driven by the
    /// Admin:SuperAdminEmails allowlist at sign-in time; tenants without it see only their own data.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// The user's preferred nav layout in the Next.js hosted dashboard. Not read by ShortLynx.Admin,
    /// which keeps its own separate, browser-local preference instead.
    /// </summary>
    public NavStyle NavStyle { get; set; }

    public virtual ICollection<MagicLinkTokenEntity> MagicLinkTokens { get; set; } = [];
    public virtual ICollection<CustomDomainEntity> CustomDomains { get; set; } = [];
    public virtual ICollection<MembershipEntity> Memberships { get; set; } = [];
}
