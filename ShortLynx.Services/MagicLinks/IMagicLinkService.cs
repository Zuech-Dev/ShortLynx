using ShortLynx.Data.Entities;

namespace ShortLynx.Services.MagicLinks;

public interface IMagicLinkService
{
    /// <summary>
    /// Looks up the UserAccount for the given email, mints a single-use token, and returns the plaintext
    /// token to embed in the magic link URL. A magic link is only issued to an existing, active user;
    /// this method never provisions a user. Returns an empty string — with no token created and no email
    /// sent — if the email doesn't belong to an active user, or if the request was throttled (the email
    /// already has the maximum number of concurrently-valid tokens).
    /// </summary>
    Task<string> CreateTokenAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Validates the token, marks it used, and returns the associated UserAccount.
    /// Returns null if the token is unknown, expired, or already used.
    /// </summary>
    Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default);
}
