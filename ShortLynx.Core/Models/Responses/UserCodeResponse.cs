namespace ShortLynx.Core.Models.Responses;

public sealed record UserCodeResponse(
    Guid UserId,
    string Code);
