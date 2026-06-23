using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Services.Users;

public sealed class UserAdminService(
    ShortLynxDbContext db, IAccountService accounts, IMagicLinkService magicLinks) : IUserAdminService
{
    public async Task<IReadOnlyList<AdminUserView>> ListUsersAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var users = await db.UserAccountEntities
            .Include(u => u.Memberships).ThenInclude(m => m.Account)
            .OrderByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return users.Select(Project).ToList();
    }

    public async Task<AdminUserView> AddUserAsync(
        string email, Guid? accountId = null, AccountRole? role = null, string? newAccountName = null,
        CancellationToken ct = default)
    {
        var normalised = Normalise(email);

        if (accountId is { } target)
        {
            // Assigning to an account: the account must already exist.
            if (!await db.AccountEntities.AnyAsync(a => a.Id == target, ct))
                throw new KeyNotFoundException($"Account '{target}' does not exist.");

            var user = await GetOrCreateUserAsync(normalised, ct);
            await UpsertMembershipAsync(user.Id, target, role ?? AccountRole.Member, ct);
            await magicLinks.CreateTokenAsync(normalised, ct);
            return await GetViewAsync(user.Id, ct);
        }

        // No account supplied: maintain the current behavior — a fresh account owned by the user.
        await accounts.CreateAccountWithOwnerAsync(
            string.IsNullOrWhiteSpace(newAccountName) ? normalised : newAccountName, normalised, ct);
        var created = await db.UserAccountEntities.SingleAsync(u => u.Email == normalised, ct);
        return await GetViewAsync(created.Id, ct);
    }

    public async Task<bool> AssignToAccountAsync(Guid userId, Guid accountId, AccountRole role, CancellationToken ct = default)
    {
        if (!await db.AccountEntities.AnyAsync(a => a.Id == accountId, ct))
            throw new KeyNotFoundException($"Account '{accountId}' does not exist.");
        if (!await db.UserAccountEntities.AnyAsync(u => u.Id == userId, ct))
            return false;

        await UpsertMembershipAsync(userId, accountId, role, ct);
        return true;
    }

    public async Task<bool> RemoveFromAccountAsync(Guid userId, Guid accountId, CancellationToken ct = default)
    {
        var removed = await db.MembershipEntities
            .Where(m => m.UserAccountId == userId && m.AccountId == accountId)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public async Task<bool> SetSuperAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
        => await db.UserAccountEntities
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsAdmin, isAdmin), ct) > 0;

    public async Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default)
        => await db.UserAccountEntities
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive), ct) > 0;

    private async Task UpsertMembershipAsync(Guid userId, Guid accountId, AccountRole role, CancellationToken ct)
    {
        var existing = await db.MembershipEntities
            .FirstOrDefaultAsync(m => m.UserAccountId == userId && m.AccountId == accountId, ct);
        if (existing is null)
        {
            db.MembershipEntities.Add(new MembershipEntity
            {
                Id = Guid.CreateVersion7(),
                AccountId = accountId,
                UserAccountId = userId,
                Role = role,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Role = role;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserAccountEntity> GetOrCreateUserAsync(string normalisedEmail, CancellationToken ct)
    {
        var user = await db.UserAccountEntities.FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct);
        if (user is not null) return user;

        user = new UserAccountEntity
        {
            Id = Guid.CreateVersion7(),
            Email = normalisedEmail,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.UserAccountEntities.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<AdminUserView> GetViewAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.UserAccountEntities
            .Include(u => u.Memberships).ThenInclude(m => m.Account)
            .SingleAsync(u => u.Id == userId, ct);
        return Project(user);
    }

    private static AdminUserView Project(UserAccountEntity u) => new(
        u.Id, u.Email, u.IsActive, u.IsAdmin, u.CreatedAt,
        u.Memberships
            .OrderByDescending(m => m.Role)
            .Select(m => new AccountSummary(m.AccountId, m.Account.Name, m.Role))
            .ToList());

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
