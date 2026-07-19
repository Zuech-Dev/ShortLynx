using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Services.Accounts;

public sealed class AccountService(ShortLynxDbContext db, IMagicLinkService magicLinks) : IAccountService
{
    public async Task<AccountEntity> CreateAccountWithOwnerAsync(string name, string ownerEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Account name is required.", nameof(name));

        var email = Normalise(ownerEmail);
        var owner = await GetOrCreateUserAsync(email, ct);

        var account = new AccountEntity
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.AccountEntities.Add(account);
        db.MembershipEntities.Add(new MembershipEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            UserAccountId = owner.Id,
            Role = AccountRole.Owner,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        // Let the new owner sign in.
        await magicLinks.CreateTokenAsync(email, ct);
        return account;
    }

    public async Task<MembershipEntity> InviteMemberAsync(
        Guid accountId, string email, AccountRole role, Guid invitedByUserAccountId, CancellationToken ct = default)
    {
        var actorRole = await GetRoleAsync(accountId, invitedByUserAccountId, ct)
            ?? throw new UnauthorizedAccessException("You are not a member of this account.");
        if (!AccountPermissions.CanManageMembers(actorRole))
            throw new UnauthorizedAccessException("You don't have permission to invite members.");
        if (!AccountPermissions.CanAssignRole(actorRole, role))
            throw new ArgumentException($"You can't grant the '{role}' role.", nameof(role));

        var normalised = Normalise(email);
        var user = await GetOrCreateUserAsync(normalised, ct);

        if (await db.MembershipEntities.AnyAsync(m => m.AccountId == accountId && m.UserAccountId == user.Id, ct))
            throw new InvalidOperationException($"'{normalised}' is already a member of this account.");

        var membership = new MembershipEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            UserAccountId = user.Id,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
            InvitedByUserAccountId = invitedByUserAccountId,
        };
        db.MembershipEntities.Add(membership);
        await db.SaveChangesAsync(ct);

        await magicLinks.CreateTokenAsync(normalised, ct);
        return membership;
    }

    public async Task<bool> ChangeRoleAsync(
        Guid accountId, Guid targetUserAccountId, AccountRole newRole, Guid actorUserAccountId, CancellationToken ct = default)
    {
        var actorRole = await GetRoleAsync(accountId, actorUserAccountId, ct);
        var target = await db.MembershipEntities
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.UserAccountId == targetUserAccountId, ct);
        if (actorRole is not { } actor || target is null)
            return false;

        // The actor must outrank the target's current role and be allowed to grant the new role.
        if (!AccountPermissions.CanManageMember(actor, target.Role) || !AccountPermissions.CanAssignRole(actor, newRole))
            return false;

        target.Role = newRole;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(
        Guid accountId, Guid targetUserAccountId, Guid actorUserAccountId, CancellationToken ct = default)
    {
        var actorRole = await GetRoleAsync(accountId, actorUserAccountId, ct);
        var target = await db.MembershipEntities
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.UserAccountId == targetUserAccountId, ct);
        if (actorRole is not { } actor || target is null)
            return false;

        if (!AccountPermissions.CanManageMember(actor, target.Role))
            return false;

        db.MembershipEntities.Remove(target);
        await db.SaveChangesAsync(ct);

        // Revoke the removed member's refresh tokens *for this account only* — their sessions in other
        // accounts must survive. Writes already fail immediately (role is checked against the DB per
        // request), but without this an ejected member's reads coast until access-token expiry and
        // their refresh token keeps working indefinitely. Legacy tokens with no stored account are
        // left alone: on refresh they re-resolve to the user's (now changed) primary account anyway.
        // Done directly on the DbContext — AccountService can't take IUserSessionService without a
        // dependency cycle (UserSessionService already depends on IAccountService).
        await db.RefreshTokenEntities
            .Where(t => t.UserAccountId == targetUserAccountId && t.AccountId == accountId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        return true;
    }

    public async Task<IReadOnlyList<MemberView>> ListMembersAsync(Guid accountId, CancellationToken ct = default)
        => await db.MembershipEntities
            .Where(m => m.AccountId == accountId)
            .OrderBy(m => m.Id)
            .Select(m => new MemberView(m.UserAccountId, m.UserAccount.Email, m.Role, m.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AccountSummary>> ListAccountsForUserAsync(Guid userAccountId, CancellationToken ct = default)
        => await db.MembershipEntities
            .Where(m => m.UserAccountId == userAccountId)
            .OrderByDescending(m => m.Role)
            .Select(m => new AccountSummary(m.AccountId, m.Account.Name, m.Role))
            .ToListAsync(ct);

    public async Task<AccountRole?> GetRoleAsync(Guid accountId, Guid userAccountId, CancellationToken ct = default)
    {
        var membership = await db.MembershipEntities
            .Where(m => m.AccountId == accountId && m.UserAccountId == userAccountId)
            .Select(m => new { m.Role })
            .FirstOrDefaultAsync(ct);
        return membership?.Role;
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

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}
