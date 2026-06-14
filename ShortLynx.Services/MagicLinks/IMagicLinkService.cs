using ShortLynx.Data.Entities;

namespace ShortLynx.Services.MagicLinks;

public interface IMagicLinkService
{
    /// <summary>
    /// Looks up or creates the UserAccount for the given email, mints a single-use token, and returns
    /// the plaintext token to embed in the magic link URL. Returns an empty string if the request was
    /// throttled (the email already has the maximum number of concurrently-valid tokens), in which case
    /// no token is created and no email is sent.
    /// </summary>
    Task<string> CreateTokenAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Validates the token, marks it used, and returns the associated UserAccount.
    /// Returns null if the token is unknown, expired, or already used.
    /// </summary>
    Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default);
}
