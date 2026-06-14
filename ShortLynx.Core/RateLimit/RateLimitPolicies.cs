namespace ShortLynx.Core.RateLimit;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit on the unauthenticated magic-link (email-sending) endpoint.</summary>
    public const string MagicLinks = "magic-links";

    /// <summary>Per-IP limit on the admin-secret-protected key provisioning endpoint (brute-force guard).</summary>
    public const string ApiKeys = "api-keys";
}
