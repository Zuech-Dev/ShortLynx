using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Auth;

/// <summary>An issued session: a short-lived JWT access token and a long-lived refresh token.</summary>
public sealed record SessionTokens(
    string AccessToken,
    DateTimeOffset AccessExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

public interface IUserSessionService
{
    /// <summary>
    /// Issues an access + refresh token pair for the user, embedding the current account and role in the
    /// access-token claims (when supplied).
    /// </summary>
    Task<SessionTokens> IssueAsync(UserAccountEntity user, Guid? accountId, AccountRole? role, CancellationToken ct = default);

    /// <summary>
    /// Validates and rotates a refresh token: returns a fresh pair (re-resolving the user's current
    /// account/role), or null if the token is unknown, expired, or revoked. Re-use of an already-revoked
    /// token revokes all of the user's tokens (reuse detection).
    /// </summary>
    Task<SessionTokens?> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes a single refresh token (logout). No-op if unknown.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Revokes all of a user's active refresh tokens.</summary>
    Task RevokeAllForUserAsync(Guid userAccountId, CancellationToken ct = default);
}
