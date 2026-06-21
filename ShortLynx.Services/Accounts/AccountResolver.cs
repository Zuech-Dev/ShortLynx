using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Accounts;

/// <summary>
/// Bridges the current single-user dashboard flow to the account model: resolves the account a user
/// acts in, lazily creating a personal account (with an Owner membership) the first time. ACC-2/ACC-4
/// formalize account creation at invite/sign-in time; this keeps the dashboard working in the meantime.
/// </summary>
public static class AccountResolver
{
    public static async Task<Guid> GetOrCreatePersonalAccountIdAsync(
        ShortLynxDbContext db, Guid userAccountId, string accountName, CancellationToken ct = default)
    {
        var existing = await db.MembershipEntities
            .Where(m => m.UserAccountId == userAccountId)
            .OrderByDescending(m => m.Role)
            .Select(m => (Guid?)m.AccountId)
            .FirstOrDefaultAsync(ct);
        if (existing is { } accountId)
            return accountId;

        var account = new AccountEntity
        {
            Id = Guid.CreateVersion7(),
            Name = accountName,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.AccountEntities.Add(account);
        db.MembershipEntities.Add(new MembershipEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            UserAccountId = userAccountId,
            Role = AccountRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return account.Id;
    }
}
