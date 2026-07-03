namespace ShortLynx.Core.Models.Responses;

/// <summary>A connected social account. Tokens are never included — they exist only encrypted at rest.</summary>
public sealed record SocialConnectionResponse(
    Guid Id,
    string Platform,
    string Handle,
    string? InstanceUrl,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);
