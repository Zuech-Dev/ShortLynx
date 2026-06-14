namespace ShortLynx.Core.Models.Responses;

/// <summary>Returned once on creation. PlaintextKey is never stored — callers must save it immediately.</summary>
public sealed record ApiKeyResponse(
    Guid Id,
    string Name,
    string PlaintextKey,
    string[] Scopes,
    DateTimeOffset CreatedAt);
