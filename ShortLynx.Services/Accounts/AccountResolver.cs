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
    /// <summary>
    /// Resolves the account the user should act in, honoring an explicitly selected account (the
    /// dashboard account switcher) when the user is still a member of it. Falls back to the primary
    /// (highest-role) account — lazily created — when no valid selection is supplied. This keeps a
    /// stale or forged selection from leaking another tenant's data: a non-member selection is ignored.
    /// </summary>
    public static async Task<Guid> ResolveAccountIdAsync(
        ShortLynxDbContext db, Guid userAccountId, Guid? selectedAccountId, string accountName,
        CancellationToken ct = default)
    {
        if (selectedAccountId is { } selected)
        {
            var isMember = await db.MembershipEntities
                .AnyAsync(m => m.AccountId == selected && m.UserAccountId == userAccountId, ct);
            if (isMember)
                return selected;
        }

        return await GetOrCreatePersonalAccountIdAsync(db, userAccountId, accountName, ct);
    }

    // Known, accepted race: two concurrent requests for a brand-new user (no membership yet) can each
    // fall through the existence check and create a separate personal account. The
    // Membership(AccountId, UserAccountId) unique index prevents *duplicate* rows for one account, but
    // not two distinct auto-created accounts. Left unguarded deliberately — both accounts are owned by
    // the same user (no data leak; cosmetic duplicate in their account switcher), the window is only a
    // user's first concurrent requests, and the only clean DB fix is a personal-account marker column,
    // which isn't worth the schema churn for this edge case. Revisit if it shows up in practice.
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
