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

    /// <summary>
    /// Whether the account may mint another custom (vanity) code right now. A per-account quota, not a
    /// boolean feature: the hosted policy decides based on plan + current usage + overage state (e.g.
    /// Free none, Starter 10 then pay-per/upgrade, Pro+ more but never unlimited). Self-host is always
    /// true. Gates new mints only — existing custom codes are never revalidated (grandfathered).
    /// </summary>
    Task<bool> CanCreateCustomCodeAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Whether a plan feature is available to the account.</summary>
    Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default);

    /// <summary>Whether the account may add another custom domain right now. A per-account count quota
    /// (1/5/15 on hosted Starter/Pro/Teams), not a boolean — <see cref="PlanFeature.CustomDomains"/> gates
    /// whether the feature exists at all, this gates adding beyond the current count. Self-host is
    /// always true.</summary>
    Task<bool> CanAddCustomDomainAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Visit-data retention window for this account, in days, or null for unlimited. Self-host
    /// is always null. The one seam a hosted limit must reach into an OSS background job (retention
    /// pruning) rather than a request path.</summary>
    Task<int?> GetRetentionDaysAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Whether the account may add another team member/seat right now. Self-host is always true.</summary>
    Task<bool> CanAddMemberAsync(Guid accountId, CancellationToken ct = default);
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
