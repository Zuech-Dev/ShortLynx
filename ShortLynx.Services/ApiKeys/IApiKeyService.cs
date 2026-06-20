using ShortLynx.Data.Entities;

namespace ShortLynx.Services.ApiKeys;

public interface IApiKeyService
{
    /// <summary>
    /// Returns the created record and the plaintext key. The plaintext is never stored — callers must
    /// surface it immediately. When <paramref name="userAccountId"/> is supplied, the key is owned by
    /// that user (required for per-tenant scoping in the admin dashboard).
    /// </summary>
    Task<(ApiKeyEntity Record, string PlaintextKey)> CreateAsync(string name, string[] scopes, Guid? userAccountId = null, CancellationToken ct = default);

    /// <summary>Returns the active, non-expired ApiKey for the given plaintext key, or null if invalid.</summary>
    Task<ApiKeyEntity?> ValidateAsync(string plaintextKey, CancellationToken ct = default);

    /// <summary>
    /// Deactivates the key (IsActive=false) if it is owned by <paramref name="userAccountId"/> and
    /// currently active. Returns true if a key was revoked, false otherwise (not owned / unknown / already revoked).
    /// </summary>
    Task<bool> RevokeAsync(Guid keyId, Guid userAccountId, CancellationToken ct = default);
}
