namespace ShortLynx.Core.Models.Responses;

/// <summary>An API key as listed for the current account (never includes the plaintext key).</summary>
public sealed record MyApiKeyResponse(Guid Id, string Name, string Prefix, string[] Scopes, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>An account the current user belongs to, with their role.</summary>
public sealed record AccountResponse(Guid Id, string Name, string Role);
