namespace ShortLynx.Core.RateLimit;

public static class RateLimitPolicies
{
    /// <summary>Per-IP limit on the unauthenticated magic-link (email-sending) endpoint.</summary>
    public const string MagicLinks = "magic-links";

    /// <summary>Per-IP limit on the admin-secret-protected key provisioning endpoint (brute-force guard).</summary>
    public const string ApiKeys = "api-keys";

    /// <summary>
    /// Per-IP limit on /auth/refresh and /auth/logout: token stuffing is otherwise free, and replaying
    /// stolen-then-rotated tokens triggers reuse-detection revocation — worth making expensive.
    /// </summary>
    public const string Refresh = "refresh";
}
