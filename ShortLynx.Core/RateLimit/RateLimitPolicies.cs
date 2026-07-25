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

    /// <summary>Per-IP limit on the custom-code availability endpoint — enumeration guard.</summary>
    public const string CustomCodeCheck = "custom-code-check";

    /// <summary>
    /// Per-IP <b>concurrency</b> cap on the SSE click feed. Unlike every other policy here this one is
    /// not about abuse rate: each stream holds a connection open for up to half an hour and polls on a
    /// timer, so the cost is measured in simultaneous connections, not requests per window. A fixed
    /// window would let a client open unlimited streams as long as it opened them slowly.
    /// </summary>
    public const string Stream = "stream";
}
