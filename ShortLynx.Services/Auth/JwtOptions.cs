namespace ShortLynx.Services.Auth;

/// <summary>
/// Settings for the user-session credentials issued after a magic-link login. Bound from the "Jwt"
/// configuration section. Cookie settings are primitives (no ASP.NET types) so this stays in Services.
/// </summary>
public sealed class JwtOptions
{
    public const string DefaultPlaceholderKey = "CHANGE-ME-use-a-32+-char-jwt-signing-key";

    /// <summary>HS256 signing secret. Required, 32+ chars, must not be the placeholder (fail-fast).</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "shortlynx";
    public string Audience { get; set; } = "shortlynx";

    /// <summary>Access-token lifetime in minutes (short-lived).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 30;

    // ── Cookie transport (for same-site frontends) ──────────────────────────
    public string AccessCookieName { get; set; } = "sl_access";
    public string RefreshCookieName { get; set; } = "sl_refresh";
    public string CsrfCookieName { get; set; } = "sl_csrf";

    /// <summary>Cookie domain (empty = host-only).</summary>
    public string CookieDomain { get; set; } = string.Empty;

    /// <summary>Require Secure cookies (HTTPS). Should be true in production.</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>SameSite policy: "Lax" (same-site), "Strict", or "None" (cross-site, requires Secure).</summary>
    public string CookieSameSite { get; set; } = "Lax";

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(Math.Max(1, AccessTokenMinutes));
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(Math.Max(1, RefreshTokenDays));

    /// <summary>Returns configuration errors (empty when valid). Used for fail-fast validation at startup.</summary>
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey))
            yield return "Jwt:SigningKey is required.";
        else if (SigningKey == DefaultPlaceholderKey)
            yield return "Jwt:SigningKey must be changed from the default placeholder value.";
        else if (SigningKey.Length < 32)
            yield return "Jwt:SigningKey must be at least 32 characters.";

        if (AccessTokenMinutes <= 0)
            yield return "Jwt:AccessTokenMinutes must be positive.";
        if (RefreshTokenDays <= 0)
            yield return "Jwt:RefreshTokenDays must be positive.";
    }

    public bool IsValid => !Validate().Any();
}
