namespace ShortLynx.Admin.Options;

public static class AdminClaims
{
    /// <summary>Custom claim type carrying "true" for super-admins.</summary>
    public const string IsAdmin = "shortlynx:isadmin";

    /// <summary>Authorization policy name gating cross-tenant pages.</summary>
    public const string SuperAdminPolicy = "SuperAdmin";
}
