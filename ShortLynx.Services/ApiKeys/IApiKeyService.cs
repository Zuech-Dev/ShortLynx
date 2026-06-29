using ShortLynx.Data.Entities;

namespace ShortLynx.Services.ApiKeys;

public interface IApiKeyService
{
    /// <summary>
    /// Returns the created record and the plaintext key. The plaintext is never stored — callers must
    /// surface it immediately. The key is owned by <paramref name="accountId"/> (resources it creates
    /// belong to that account); <paramref name="createdByUserAccountId"/> is recorded for audit only.
    /// </summary>
    Task<(ApiKeyEntity Record, string PlaintextKey)> CreateAsync(string name, string[] scopes, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default);

    /// <summary>Returns the active, non-expired ApiKey for the given plaintext key, or null if invalid.</summary>
    Task<ApiKeyEntity?> ValidateAsync(string plaintextKey, CancellationToken ct = default);

    /// <summary>
    /// Deactivates the key (IsActive=false) if it belongs to <paramref name="accountId"/> and is
    /// currently active. Returns true if a key was revoked, false otherwise (not owned / unknown / already revoked).
    /// </summary>
    Task<bool> RevokeAsync(Guid keyId, Guid accountId, CancellationToken ct = default);
}
