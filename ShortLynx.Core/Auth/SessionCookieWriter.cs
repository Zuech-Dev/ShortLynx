using ShortLynx.Services.Auth;

namespace ShortLynx.Core.Auth;

/// <summary>
/// Writes/clears the access, refresh, and CSRF cookies for an issued session. Extracted out of
/// AuthController so a second endpoint that also issues a session (MeController's switch-account)
/// doesn't duplicate cookie-attribute logic — getting SameSite/Secure/Domain wrong independently in two
/// places is exactly the kind of drift that's already bitten this codebase once (see the ForwardLimit
/// and third-party-cookie fixes).
/// </summary>
public static class SessionCookieWriter
{
    public static void SetSessionCookies(HttpResponse response, SessionTokens tokens, JwtOptions jwt)
    {
        response.Cookies.Append(jwt.AccessCookieName, tokens.AccessToken, CookieOptions(jwt, tokens.AccessExpiresAt));
        response.Cookies.Append(jwt.RefreshCookieName, tokens.RefreshToken, CookieOptions(jwt, tokens.RefreshExpiresAt));

        // Non-httpOnly CSRF token (double-submit): the SPA reads it and echoes it in the X-CSRF-Token header.
        var csrf = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var csrfOptions = CookieOptions(jwt, tokens.RefreshExpiresAt);
        csrfOptions.HttpOnly = false;
        response.Cookies.Append(jwt.CsrfCookieName, csrf, csrfOptions);
    }

    public static void ClearSessionCookies(HttpResponse response, JwtOptions jwt)
    {
        var expired = CookieOptions(jwt, DateTimeOffset.UtcNow.AddDays(-1));
        response.Cookies.Append(jwt.AccessCookieName, "", expired);
        response.Cookies.Append(jwt.RefreshCookieName, "", expired);
        var csrfExpired = CookieOptions(jwt, DateTimeOffset.UtcNow.AddDays(-1));
        csrfExpired.HttpOnly = false;
        response.Cookies.Append(jwt.CsrfCookieName, "", csrfExpired);
    }

    private static CookieOptions CookieOptions(JwtOptions jwt, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = jwt.CookieSecure,
        SameSite = Enum.TryParse<SameSiteMode>(jwt.CookieSameSite, ignoreCase: true, out var s) ? s : SameSiteMode.Lax,
        Domain = string.IsNullOrWhiteSpace(jwt.CookieDomain) ? null : jwt.CookieDomain,
        Path = "/",
        Expires = expires,
    };
}
