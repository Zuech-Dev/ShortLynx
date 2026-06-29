namespace ShortLynx.Core.Auth;

/// <summary>Names of the authorization policies registered in <c>Program.cs</c>.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Requires the platform super-admin (is_admin) claim — gates the /admin/* surface.</summary>
    public const string SuperAdmin = "SuperAdmin";
}
