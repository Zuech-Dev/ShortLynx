using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Services.Auth;

public sealed class UserSessionService(
    ShortLynxDbContext db,
    IAccountService accounts,
    IOptions<JwtOptions> options) : IUserSessionService
{
    private readonly JwtOptions _opts = options.Value;

    public async Task<SessionTokens> IssueAsync(
        UserAccountEntity user, Guid? accountId, AccountRole? role, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var accessExpires = now + _opts.AccessTokenLifetime;
        var accessToken = CreateAccessToken(user, accountId, role, now, accessExpires);

        var (refreshPlaintext, refreshExpires) = await CreateRefreshTokenAsync(user.Id, now, ct);
        return new SessionTokens(accessToken, accessExpires, refreshPlaintext, refreshExpires);
    }

    public async Task<SessionTokens?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokenEntities.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null) return null;

        var now = DateTimeOffset.UtcNow;

        // Reuse detection: presenting an already-revoked token means it may have been stolen — revoke
        // every active token for the user and refuse.
        if (stored.RevokedAt is not null)
        {
            await RevokeAllForUserAsync(stored.UserAccountId, ct);
            return null;
        }
        if (stored.ExpiresAt < now) return null;

        var user = await db.UserAccountEntities.FirstOrDefaultAsync(u => u.Id == stored.UserAccountId, ct);
        if (user is null) return null;

        // Rotate: mint a new pair, revoke the presented token and link it to its replacement.
        var primary = (await accounts.ListAccountsForUserAsync(user.Id, ct)).FirstOrDefault();
        var accessExpires = now + _opts.AccessTokenLifetime;
        var accessToken = CreateAccessToken(user, primary?.AccountId, primary?.Role, now, accessExpires);
        var (refreshPlaintext, refreshExpires, newId) = await CreateRefreshTokenReturningIdAsync(user.Id, now, ct);

        stored.RevokedAt = now;
        stored.ReplacedByTokenId = newId;
        await db.SaveChangesAsync(ct);

        return new SessionTokens(accessToken, accessExpires, refreshPlaintext, refreshExpires);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        await db.RefreshTokenEntities
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task RevokeAllForUserAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await db.RefreshTokenEntities
            .Where(t => t.UserAccountId == userAccountId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string CreateAccessToken(
        UserAccountEntity user, Guid? accountId, AccountRole? role, DateTimeOffset now, DateTimeOffset expires)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtClaims.Subject] = user.Id.ToString(),
            [JwtClaims.Email] = user.Email,
        };
        if (accountId is { } aid) claims[JwtClaims.AccountId] = aid.ToString();
        if (role is { } r) claims[JwtClaims.Role] = r.ToString();
        if (user.IsAdmin) claims[JwtClaims.IsAdmin] = "true";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opts.Issuer,
            Audience = _opts.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<(string Plaintext, DateTimeOffset Expires)> CreateRefreshTokenAsync(
        Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var (plaintext, expires, _) = await CreateRefreshTokenReturningIdAsync(userId, now, ct);
        return (plaintext, expires);
    }

    private async Task<(string Plaintext, DateTimeOffset Expires, Guid Id)> CreateRefreshTokenReturningIdAsync(
        Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // base64url
        var expires = now + _opts.RefreshTokenLifetime;
        var entity = new RefreshTokenEntity
        {
            Id = Guid.CreateVersion7(),
            UserAccountId = userId,
            TokenHash = Hash(plaintext),
            CreatedAt = now,
            ExpiresAt = expires,
        };
        db.RefreshTokenEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return (plaintext, expires, entity.Id);
    }

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
