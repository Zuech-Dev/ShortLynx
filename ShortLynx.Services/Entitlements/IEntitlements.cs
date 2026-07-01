namespace ShortLynx.Services.Entitlements;

/// <summary>
/// Plan / quota policy seam. The open-source build ships <see cref="UnlimitedEntitlements"/> — everything
/// allowed — so a **self-hosted instance is fully featured and unrestricted at every tier**. A hosted
/// deployment replaces this one registration with a billing-backed implementation; that enforcement code
/// lives outside this repository. Nothing in this repo paywalls a self-hosted install.
/// </summary>
public interface IEntitlements
{
    /// <summary>Whether the account may create another link (link-count quota).</summary>
    Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Whether a plan feature is available to the account.</summary>
    Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default);
}

/// <summary>Gate-able capabilities. Names are stable identifiers a hosted plan policy maps to tiers.</summary>
public enum PlanFeature
{
    CustomDomains,
    UserAttributedLinks,
    Campaigns,
    SocialPublishing,
    Conversions,
    ApiAccess,
}
