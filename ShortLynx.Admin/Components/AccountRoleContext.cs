using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Admin.Extensions;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Admin.Components;

/// <summary>
/// The signed-in user's identity and current role in the account they're acting in — resolved fresh
/// from the database (not from circuit state or claims), so a demotion applies on the next render,
/// not the next sign-in. Dashboard pages resolve this once in <c>OnInitializedAsync</c> and must use
/// it in **both** places: to hide write UI *and* as an early-return guard inside every write handler.
/// Blazor Server executes handlers regardless of whether the button that invokes them was rendered,
/// so markup-only gating is not enforcement.
/// </summary>
public sealed record AccountRoleContext(Guid UserId, Guid AccountId, AccountRole Role)
{
    /// <summary>Create / edit / delete links, domains, campaigns, social connections, API keys.</summary>
    public bool CanManageResources => AccountPermissions.CanManageResources(Role);

    /// <summary>Account-level configuration (e.g. Settings' privacy/terms URLs).</summary>
    public bool CanManageAccount => AccountPermissions.CanManageAccount(Role);

    public bool CanManageMembers => AccountPermissions.CanManageMembers(Role);

    /// <summary>
    /// Resolves the acting account (honoring the account switcher via <see cref="AccountResolver"/>)
    /// and the user's role in it. Returns null when the principal has no user id (not signed in).
    /// </summary>
    public static async Task<AccountRoleContext?> ResolveAsync(
        ShortLynxDbContext db, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.GetUserId();
        if (userId is null) return null;

        var accountId = await AccountResolver.ResolveAccountIdAsync(
            db, userId.Value, principal.GetAccountId(), principal.Identity?.Name ?? "Personal", ct);

        var role = await db.MembershipEntities
            .Where(m => m.AccountId == accountId && m.UserAccountId == userId.Value)
            .Select(m => (AccountRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        // No membership row shouldn't happen (AccountResolver guarantees one), but fail read-only.
        return new(userId.Value, accountId, role ?? AccountRole.Viewer);
    }
}
