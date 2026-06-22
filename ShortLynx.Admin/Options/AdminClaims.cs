namespace ShortLynx.Admin.Options;

public static class AdminClaims
{
    /// <summary>Custom claim type carrying "true" for super-admins.</summary>
    public const string IsAdmin = "shortlynx:isadmin";

    /// <summary>Custom claim type carrying the signed-in user's current account id.</summary>
    public const string AccountId = "shortlynx:account_id";

    /// <summary>Custom claim type carrying the user's role in the current account.</summary>
    public const string AccountRole = "shortlynx:account_role";

    /// <summary>Authorization policy name gating cross-tenant pages.</summary>
    public const string SuperAdminPolicy = "SuperAdmin";
}
