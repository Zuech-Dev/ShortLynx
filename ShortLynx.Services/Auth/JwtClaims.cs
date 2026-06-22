namespace ShortLynx.Services.Auth;

/// <summary>Claim names used in the access-token JWT (shared by the issuer and the bearer handler).</summary>
public static class JwtClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string AccountId = "account_id";
    public const string Role = "role";
    public const string IsAdmin = "is_admin";
}
