using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.ApiKeys;

public sealed class ApiKeyService(ShortLynxDbContext db, IOptions<ApiKeyOptions> options) : IApiKeyService
{
    public async Task<(ApiKeyEntity Record, string PlaintextKey)> CreateAsync(
        string name, string[] scopes, Guid accountId, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        if (!await db.AccountEntities.AnyAsync(a => a.Id == accountId, ct))
            throw new ArgumentException($"No account exists with id {accountId}.", nameof(accountId));

        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToHexString(keyBytes); // 64-char hex key
        var prefix = plaintext[..8];
        var keyHash = ComputeHmac(plaintext);

        var entity = new ApiKeyEntity
        {
            Id = Guid.CreateVersion7(),
            Prefix = prefix,
            KeyHash = keyHash,
            Name = name,
            Scopes = string.Join(",", scopes),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            AccountId = accountId,
            UserAccountId = createdByUserAccountId,
        };

        db.ApiKeyEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return (entity, plaintext);
    }

    public async Task<ApiKeyEntity?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
    {
        if (plaintextKey.Length < 8) return null;

        var prefix = plaintextKey[..8];
        // A prefix is not unique, so two active keys can share one. Check every candidate rather than
        // FirstOrDefault, otherwise a colliding key could be silently unusable. The set is ~1 in practice.
        var candidates = await db.ApiKeyEntities
            .Where(k => k.Prefix == prefix && k.IsActive)
            .ToListAsync(ct);

        var expectedHash = Convert.FromHexString(ComputeHmac(plaintextKey));
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in candidates)
        {
            if (candidate.ExpiresAt.HasValue && candidate.ExpiresAt.Value < now) continue;

            // Constant-time comparison to prevent timing attacks.
            if (CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(candidate.KeyHash),
                    expectedHash))
                return candidate;
        }

        return null;
    }

    public async Task<bool> RevokeAsync(Guid keyId, Guid accountId, CancellationToken ct = default)
    {
        // Account-scoped, atomic revoke. No DateTimeOffset in the predicate (SQLite-safe).
        var affected = await db.ApiKeyEntities
            .Where(k => k.Id == keyId && k.AccountId == accountId && k.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsActive, false), ct);
        return affected > 0;
    }

    private string ComputeHmac(string input)
    {
        var secret = Encoding.UTF8.GetBytes(options.Value.HmacSecret);
        var data = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(HMACSHA256.HashData(secret, data));
    }
}
