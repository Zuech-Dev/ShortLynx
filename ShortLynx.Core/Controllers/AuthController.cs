using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Core.RateLimit;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public class AuthController(
    IMagicLinkService magicLinks,
    IUserSessionService sessions,
    IAccountService accounts,
    ShortLynxDbContext db,
    IOptions<AccessControlOptions> accessControl,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    private JwtOptions Jwt => jwtOptions.Value;

    // POST /auth/magic-link — request a sign-in email. Always 204 (no email enumeration).
    [HttpPost("magic-link")]
    [EnableRateLimiting(RateLimitPolicies.MagicLinks)]
    public async Task<IActionResult> RequestMagicLink([FromBody] RequestMagicLinkRequest request, CancellationToken ct)
    {
        await magicLinks.CreateTokenAsync(request.Email, ct);
        return NoContent();
    }

    // POST /auth/session — exchange a magic-link token for a session.
    [HttpPost("session")]
    [EnableRateLimiting(RateLimitPolicies.MagicLinks)]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request, CancellationToken ct)
    {
        var user = await magicLinks.ValidateTokenAsync(request.Token, ct);
        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "This link is invalid, already used, or expired." });

        // Gate: allowlisted email OR a member of at least one account.
        var userAccounts = await accounts.ListAccountsForUserAsync(user.Id, ct);
        if (!accessControl.Value.IsAllowed(user.Email) && userAccounts.Count == 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account is not authorised." });

        // Every session needs an account to act in. Members use their highest-role one; an allowlisted
        // user without a membership (e.g. a bootstrap super-admin) gets a personal account here.
        Guid accountId;
        AccountRole role;
        if (userAccounts.Count > 0)
        {
            accountId = userAccounts[0].AccountId;
            role = userAccounts[0].Role;
        }
        else
        {
            accountId = await AccountResolver.GetOrCreatePersonalAccountIdAsync(db, user.Id, user.Email, ct);
            role = AccountRole.Owner;
        }

        var tokens = await sessions.IssueAsync(user, accountId, role, ct);
        SetSessionCookies(tokens);

        return Ok(new SessionResponse(
            tokens.AccessToken,
            tokens.RefreshToken,
            ExpiresInSeconds(tokens.AccessExpiresAt),
            new UserSummary(user.Id, user.Email, user.IsAdmin, accountId, role.ToString())));
    }

    // POST /auth/refresh — rotate the refresh token (from body or cookie) into a fresh pair.
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var refreshToken = request?.RefreshToken ?? Request.Cookies[Jwt.RefreshCookieName];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { error = "No refresh token." });

        var tokens = await sessions.RefreshAsync(refreshToken, ct);
        if (tokens is null)
        {
            ClearSessionCookies();
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }

        SetSessionCookies(tokens);
        return Ok(new RefreshResponse(tokens.AccessToken, tokens.RefreshToken, ExpiresInSeconds(tokens.AccessExpiresAt)));
    }

    // POST /auth/logout — revoke the refresh token and clear cookies.
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var refreshToken = request?.RefreshToken ?? Request.Cookies[Jwt.RefreshCookieName];
        if (!string.IsNullOrEmpty(refreshToken))
            await sessions.RevokeAsync(refreshToken, ct);
        ClearSessionCookies();
        return NoContent();
    }

    // GET /auth/me — the current session's user (from the validated access token).
    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult Me()
    {
        var id = User.FindFirst(JwtClaims.Subject)?.Value;
        if (id is null) return Unauthorized();
        return Ok(new UserSummary(
            Guid.Parse(id),
            User.FindFirst(JwtClaims.Email)?.Value ?? "",
            User.FindFirst(JwtClaims.IsAdmin)?.Value == "true",
            Guid.TryParse(User.FindFirst(JwtClaims.AccountId)?.Value, out var acc) ? acc : null,
            User.FindFirst(JwtClaims.Role)?.Value));
    }

    private static int ExpiresInSeconds(DateTimeOffset at)
        => Math.Max(0, (int)(at - DateTimeOffset.UtcNow).TotalSeconds);

    private void SetSessionCookies(SessionTokens tokens)
    {
        Response.Cookies.Append(Jwt.AccessCookieName, tokens.AccessToken, CookieOptions(tokens.AccessExpiresAt));
        Response.Cookies.Append(Jwt.RefreshCookieName, tokens.RefreshToken, CookieOptions(tokens.RefreshExpiresAt));

        // Non-httpOnly CSRF token (double-submit): the SPA reads it and echoes it in the X-CSRF-Token header.
        var csrf = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var csrfOptions = CookieOptions(tokens.RefreshExpiresAt);
        csrfOptions.HttpOnly = false;
        Response.Cookies.Append(Jwt.CsrfCookieName, csrf, csrfOptions);
    }

    private void ClearSessionCookies()
    {
        var expired = CookieOptions(DateTimeOffset.UtcNow.AddDays(-1));
        Response.Cookies.Append(Jwt.AccessCookieName, "", expired);
        Response.Cookies.Append(Jwt.RefreshCookieName, "", expired);
        var csrfExpired = CookieOptions(DateTimeOffset.UtcNow.AddDays(-1));
        csrfExpired.HttpOnly = false;
        Response.Cookies.Append(Jwt.CsrfCookieName, "", csrfExpired);
    }

    private CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = Jwt.CookieSecure,
        SameSite = Enum.TryParse<SameSiteMode>(Jwt.CookieSameSite, ignoreCase: true, out var s) ? s : SameSiteMode.Lax,
        Domain = string.IsNullOrWhiteSpace(Jwt.CookieDomain) ? null : Jwt.CookieDomain,
        Path = "/",
        Expires = expires,
    };
}
