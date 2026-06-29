namespace ShortLynx.Core.Models.Responses;

public sealed record UserSummary(Guid Id, string Email, bool IsAdmin, Guid? AccountId, string? Role);

/// <summary>
/// Returned from POST /auth/session. The tokens are also set as httpOnly cookies — same-site frontends
/// should rely on the cookies and ignore the body tokens; cross-origin clients use the body tokens
/// (access via the Authorization header, refresh via /auth/refresh).
/// </summary>
public sealed record SessionResponse(string AccessToken, string RefreshToken, int ExpiresIn, UserSummary User);

/// <summary>Returned from POST /auth/refresh.</summary>
public sealed record RefreshResponse(string AccessToken, string RefreshToken, int ExpiresIn);
