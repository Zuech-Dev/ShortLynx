namespace ShortLynx.Admin.Options;

/// <summary>
/// Controls who may sign into the admin dashboard and who may see cross-tenant data.
/// Bound from the "Admin" configuration section.
/// </summary>
public sealed class AdminOptions
{
    /// <summary>
    /// Emails permitted to sign in at all. A user must match this list (or <see cref="SuperAdminEmails"/>)
    /// to complete the magic-link flow. Empty ⇒ no one can sign in (fail closed).
    /// </summary>
    public string[] AllowedEmails { get; set; } = [];

    /// <summary>
    /// Subset of users who may view cross-tenant pages (all users, global totals). Implicitly allowed
    /// to sign in. Matched case-insensitively against the normalised (trimmed, lower-cased) email.
    /// </summary>
    public string[] SuperAdminEmails { get; set; } = [];

    public bool IsAllowed(string normalisedEmail) =>
        AllowedEmails.Contains(normalisedEmail, StringComparer.OrdinalIgnoreCase)
        || IsSuperAdmin(normalisedEmail);

    public bool IsSuperAdmin(string normalisedEmail) =>
        SuperAdminEmails.Contains(normalisedEmail, StringComparer.OrdinalIgnoreCase);
}
