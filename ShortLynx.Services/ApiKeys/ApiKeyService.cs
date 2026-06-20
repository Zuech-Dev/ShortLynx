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
        string name, string[] scopes, Guid? userAccountId = null, CancellationToken ct = default)
    {
        if (userAccountId is { } uid &&
            !await db.UserAccountEntities.AnyAsync(u => u.Id == uid, ct))
            throw new ArgumentException($"No user account exists with id {uid}.", nameof(userAccountId));

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
            UserAccountId = userAccountId,
        };

        db.ApiKeyEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return (entity, plaintext);
    }

    public async Task<ApiKeyEntity?> ValidateAsync(string plaintextKey, CancellationToken ct = default)
    {
        if (plaintextKey.Length < 8) return null;

        var prefix = plaintextKey[..8];
        var candidate = await db.ApiKeyEntities
            .Where(k => k.Prefix == prefix && k.IsActive)
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return null;
        if (candidate.ExpiresAt.HasValue && candidate.ExpiresAt.Value < DateTimeOffset.UtcNow) return null;

        var expectedHash = ComputeHmac(plaintextKey);
        // Constant-time comparison to prevent timing attacks.
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(candidate.KeyHash),
                Convert.FromHexString(expectedHash)))
            return null;

        return candidate;
    }

    public async Task<bool> RevokeAsync(Guid keyId, Guid userAccountId, CancellationToken ct = default)
    {
        // Ownership-scoped, atomic revoke. No DateTimeOffset in the predicate (SQLite-safe).
        var affected = await db.ApiKeyEntities
            .Where(k => k.Id == keyId && k.UserAccountId == userAccountId && k.IsActive)
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
