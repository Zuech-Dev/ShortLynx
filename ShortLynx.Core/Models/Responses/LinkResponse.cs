namespace ShortLynx.Core.Models.Responses;

public sealed record LinkResponse(
    Guid Id,
    string Url,
    string Mode,
    string ShortCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
