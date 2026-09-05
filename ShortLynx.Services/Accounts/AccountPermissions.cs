using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Accounts;

/// <summary>An action a member may attempt within an account.</summary>
public enum AccountAction
{
    /// <summary>View links, domains, API keys, and analytics.</summary>
    ReadResources,
    /// <summary>Create / edit / delete links, campaigns, and folders.</summary>
    ManageResources,
    /// <summary>Create/edit/delete custom domains, API keys, and social connections.</summary>
    ManageIntegrations,
    /// <summary>Invite, change the role of, or remove members.</summary>
    ManageMembers,
    /// <summary>Rename, deactivate, transfer, or delete the account.</summary>
    ManageAccount,
}

/// <summary>
/// Single source of truth for what each <see cref="AccountRole"/> may do — used by both the dashboard
/// (to gate UI) and the API (to gate endpoints) so the rules never drift.
/// </summary>
public static class AccountPermissions
{
    public static bool Can(AccountRole role, AccountAction action) => action switch
    {
        AccountAction.ReadResources => role >= AccountRole.Viewer,
        AccountAction.ManageResources => role >= AccountRole.Member,
        AccountAction.ManageIntegrations => role >= AccountRole.Admin,
        AccountAction.ManageMembers => role >= AccountRole.Admin,
        AccountAction.ManageAccount => role >= AccountRole.Owner,
        _ => false,
    };

    public static bool CanReadResources(AccountRole role) => Can(role, AccountAction.ReadResources);
    public static bool CanManageResources(AccountRole role) => Can(role, AccountAction.ManageResources);
    public static bool CanManageIntegrations(AccountRole role) => Can(role, AccountAction.ManageIntegrations);
    public static bool CanManageMembers(AccountRole role) => Can(role, AccountAction.ManageMembers);
    public static bool CanManageAccount(AccountRole role) => Can(role, AccountAction.ManageAccount);

    /// <summary>
    /// True if <paramref name="actor"/> may act on a member who currently holds <paramref name="target"/>.
    /// Requires member-management rights and that the actor strictly outranks the target (so an Admin
    /// can't touch another Admin or the Owner).
    /// </summary>
    public static bool CanManageMember(AccountRole actor, AccountRole target)
        => CanManageMembers(actor) && actor > target;

    /// <summary>
    /// True if <paramref name="actor"/> may grant <paramref name="roleToAssign"/>. The actor can only
    /// grant a role strictly below their own (no privilege escalation; Owner is never granted via invite).
    /// </summary>
    public static bool CanAssignRole(AccountRole actor, AccountRole roleToAssign)
        => CanManageMembers(actor) && roleToAssign < actor;
}
