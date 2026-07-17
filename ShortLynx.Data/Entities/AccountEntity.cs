using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A workspace/tenant that owns resources (links, custom domains, API keys). Users access an account
/// through a <see cref="MembershipEntity"/> carrying a role.
/// </summary>
[Table("Accounts")]
public class AccountEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    // Privacy & compliance (TRACKING_DISCLOSURE_PLAN): when PrivacyPolicyUrl is unset, Mode 2
    // (user-attributed) redirects pause on a ShortLynx disclosure interstitial where the recipient
    // chooses to continue tracked, continue anonymized, or cancel. Setting the URL asserts the
    // operator discloses link tracking themselves (they must confirm this in the dashboard).
    public string? PrivacyPolicyUrl { get; set; }
    public string? TermsOfServiceUrl { get; set; }

    public virtual ICollection<MembershipEntity> Memberships { get; set; } = [];
}
