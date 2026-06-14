using System.Security.Claims;
using ShortLynx.Admin.Options;

namespace ShortLynx.Admin.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in user's UserAccount id (from the NameIdentifier claim), or null.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>True if the user carries the super-admin claim (may view cross-tenant data).</summary>
    public static bool IsSuperAdmin(this ClaimsPrincipal user)
        => user.HasClaim(AdminClaims.IsAdmin, "true");
}
