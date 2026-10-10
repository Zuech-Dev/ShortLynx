namespace ShortLynx.Core.Models.Responses;

/// <summary>
/// What the current account's plan allows right now, so a client can hide what it can't use instead of
/// letting the user hit a 402. Advisory only — every create path still enforces on the server. On
/// self-host (UnlimitedEntitlements) every flag is true and <see cref="RetentionDays"/> is null.
/// </summary>
/// <param name="CanCreateLink">Under the link quota (Free hard-caps; paid tiers meter overage instead).</param>
/// <param name="CustomCodes">Can mint another custom (vanity) code now — plan AND remaining quota.</param>
/// <param name="CustomDomains">The plan includes custom domains at all.</param>
/// <param name="CanAddCustomDomain">Room for another custom domain under the plan's count.</param>
/// <param name="CanAddMember">Room for another seat.</param>
/// <param name="RetentionDays">Click history visible in analytics, in days; null = unlimited.</param>
/// <param name="StyledQr">The plan includes styled QR codes (see <c>PlanFeature.StyledQr</c>).</param>
public sealed record EntitlementsResponse(
    bool CanCreateLink,
    bool CustomCodes,
    bool UserAttributedLinks,
    bool Campaigns,
    bool CustomDomains,
    bool CanAddCustomDomain,
    bool SocialPublishing,
    bool ApiAccess,
    bool Conversions,
    bool CanAddMember,
    int? RetentionDays,
    bool StyledQr);
