namespace ShortLynx.Services.Auth;

/// <summary>
/// Who may obtain a session: an allowlist of emails permitted to sign in (bootstrap owners) and the
/// subset who are platform super-admins. Membership in any account also grants sign-in (checked
/// separately by the caller). Bound from the "Admin" configuration section — shared by Core and Admin.
/// </summary>
public sealed class AccessControlOptions
{
    public string[] AllowedEmails { get; set; } = [];
    public string[] SuperAdminEmails { get; set; } = [];

    public bool IsAllowed(string normalisedEmail) =>
        AllowedEmails.Contains(normalisedEmail, StringComparer.OrdinalIgnoreCase) || IsSuperAdmin(normalisedEmail);

    public bool IsSuperAdmin(string normalisedEmail) =>
        SuperAdminEmails.Contains(normalisedEmail, StringComparer.OrdinalIgnoreCase);
}
