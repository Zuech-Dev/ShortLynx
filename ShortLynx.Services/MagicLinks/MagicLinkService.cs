using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Email;

namespace ShortLynx.Services.MagicLinks;

public sealed class MagicLinkService(
    ShortLynxDbContext db,
    IEmailSender emailSender,
    IOptions<MagicLinkOptions> options) : IMagicLinkService
{
    public async Task<string> CreateTokenAsync(string email, CancellationToken ct = default)
    {
        var normalised = email.Trim().ToLowerInvariant();

        var user = await db.UserAccountEntities
            .FirstOrDefaultAsync(u => u.Email == normalised, ct);

        if (user is null)
        {
            user = new UserAccountEntity
            {
                Id = Guid.CreateVersion7(),
                Email = normalised,
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true,
            };
            db.UserAccountEntities.Add(user);
            await db.SaveChangesAsync(ct);
        }

        // Per-email throttle: cap concurrently-valid tokens so the endpoint can't be used to bomb a
        // single address or grow the table unboundedly. Silently drop once the cap is reached.
        // ExpiresAt is compared in memory — SQLite can't translate DateTimeOffset comparisons in SQL,
        // and the unused-token set per user is tiny.
        var now = DateTimeOffset.UtcNow;
        var unusedExpiries = await db.MagicLinkTokenEntities
            .Where(t => t.UserAccountId == user.Id && t.UsedAt == null)
            .Select(t => t.ExpiresAt)
            .ToListAsync(ct);
        if (unusedExpiries.Count(e => e > now) >= options.Value.MaxActiveTokensPerUser)
            return string.Empty;

        // 32 bytes → 256-bit token space; no salt needed for hash storage.
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // Base64Url

        var tokenEntity = new MagicLinkTokenEntity
        {
            Id = Guid.CreateVersion7(),
            UserAccountId = user.Id,
            TokenHash = HashToken(plaintext),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(options.Value.TokenExpiryMinutes),
        };

        db.MagicLinkTokenEntities.Add(tokenEntity);
        await db.SaveChangesAsync(ct);

        var link = string.IsNullOrEmpty(options.Value.ConfirmationUrlBase)
            ? plaintext
            : $"{options.Value.ConfirmationUrlBase}?token={plaintext}";

        var html = $"""
            <p>Click the link below to sign in to ShortLynx. This link expires in {options.Value.TokenExpiryMinutes} minutes and can only be used once.</p>
            <p><a href="{link}">Sign in to ShortLynx</a></p>
            <p>If you did not request this link, you can safely ignore this email.</p>
            """;

        await emailSender.SendAsync(normalised, "Your ShortLynx sign-in link", html, ct);

        return plaintext;
    }

    public async Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        var hash = HashToken(token);

        var tokenEntity = await db.MagicLinkTokenEntities
            .Include(t => t.UserAccount)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (tokenEntity is null) return null;
        if (tokenEntity.UsedAt.HasValue) return null;
        if (tokenEntity.ExpiresAt < DateTimeOffset.UtcNow) return null;

        // Atomically claim the token: only the request that flips UsedAt from null wins, so two
        // concurrent redemptions can't both succeed. No DateTimeOffset in the predicate (SQLite-safe);
        // the expiry check above runs in C# since SQLite can't translate it in SQL.
        var claimed = await db.MagicLinkTokenEntities
            .Where(t => t.Id == tokenEntity.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTimeOffset.UtcNow), ct);

        if (claimed == 0) return null;
        return tokenEntity.UserAccount;
    }

    private static string HashToken(string plaintext)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
