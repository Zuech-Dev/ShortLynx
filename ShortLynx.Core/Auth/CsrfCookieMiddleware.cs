using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Auth;

namespace ShortLynx.Core.Auth;

/// <summary>
/// Double-submit CSRF guard for cookie-authenticated requests. Browsers auto-send the access cookie but
/// can't set a custom header cross-site, so an unsafe request authenticated via the access cookie must
/// also carry an <c>X-CSRF-Token</c> header matching the (non-httpOnly) CSRF cookie. Requests using an
/// explicit <c>Authorization: Bearer</c> header (API keys, cross-origin JWT clients) are exempt.
/// </summary>
public sealed class CsrfCookieMiddleware(RequestDelegate next, IOptions<JwtOptions> options)
{
    public const string HeaderName = "X-CSRF-Token";

    private static readonly HashSet<string> UnsafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(HttpContext context)
    {
        if (UnsafeMethods.Contains(context.Request.Method))
        {
            var opts = options.Value;
            var hasBearerHeader = context.Request.Headers.Authorization
                .ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

            // Only guard cookie-authenticated requests.
            if (!hasBearerHeader && context.Request.Cookies.ContainsKey(opts.AccessCookieName))
            {
                var headerToken = context.Request.Headers[HeaderName].ToString();
                var cookieToken = context.Request.Cookies[opts.CsrfCookieName] ?? "";
                if (string.IsNullOrEmpty(headerToken) || !FixedEquals(headerToken, cookieToken))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = "CSRF token missing or invalid." });
                    return;
                }
            }
        }

        await next(context);
    }

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
