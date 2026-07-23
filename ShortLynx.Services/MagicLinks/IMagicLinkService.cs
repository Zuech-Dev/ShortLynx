using ShortLynx.Data.Entities;

namespace ShortLynx.Services.MagicLinks;

public interface IMagicLinkService
{
    /// <summary>
    /// Mints a single-use token and returns the plaintext to embed in the magic link URL. A link is
    /// issued to an existing active user, or to an **allowlisted / super-admin email** (which is
    /// provisioned here — the first-admin bootstrap on a fresh install). Returns an empty string — no
    /// token, no email — for any other unknown email, a deactivated account, or when the request is
    /// throttled (the email already has the maximum number of concurrently-valid tokens). Arbitrary
    /// emails are never provisioned and get no email, so the endpoint can't enumerate or be abused.
    /// </summary>
    Task<string> CreateTokenAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Validates the token, marks it used, and returns the associated UserAccount.
    /// Returns null if the token is unknown, expired, or already used.
    /// </summary>
    Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default);
}
