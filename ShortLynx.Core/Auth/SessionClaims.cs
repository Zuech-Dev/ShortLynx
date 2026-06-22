using System.Security.Claims;
using ShortLynx.Services.Auth;

namespace ShortLynx.Core.Auth;

/// <summary>Reads the session (JWT) claims set by <c>UserSessionService</c>.</summary>
public static class SessionClaims
{
    public static Guid UserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(JwtClaims.Subject)!);

    /// <summary>The account the session is acting in (always present for a session token).</summary>
    public static Guid AccountId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(JwtClaims.AccountId)!);

    public static string Email(this ClaimsPrincipal user)
        => user.FindFirstValue(JwtClaims.Email) ?? "";

    public static string? Role(this ClaimsPrincipal user)
        => user.FindFirstValue(JwtClaims.Role);

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.FindFirstValue(JwtClaims.IsAdmin) == "true";
}
