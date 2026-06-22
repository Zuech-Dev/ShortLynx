using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Exchanges a validated magic-link token for a session.</summary>
public sealed record CreateSessionRequest([Required] string Token);

/// <summary>Refresh/logout body. The refresh token may also come from the refresh cookie.</summary>
public sealed record RefreshRequest(string? RefreshToken);
