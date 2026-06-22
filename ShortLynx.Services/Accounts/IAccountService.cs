using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Accounts;

/// <summary>A member of an account, for listing.</summary>
public sealed record MemberView(Guid UserAccountId, string Email, AccountRole Role, DateTimeOffset CreatedAt);

/// <summary>An account a user belongs to, with the user's role in it.</summary>
public sealed record AccountSummary(Guid AccountId, string Name, AccountRole Role);

public interface IAccountService
{
    /// <summary>
    /// Creates a new account with the given user as its Owner (the superuser "add user" path), sending
    /// the owner a magic-link sign-in email. Caller is responsible for restricting this to super-admins.
    /// </summary>
    Task<AccountEntity> CreateAccountWithOwnerAsync(string name, string ownerEmail, CancellationToken ct = default);

    /// <summary>
    /// Invites a user to <paramref name="accountId"/> with <paramref name="role"/>, creating the user if
    /// new and emailing them a sign-in link. Enforces that the inviter may manage members and may grant
    /// the role. Throws <see cref="UnauthorizedAccessException"/> / <see cref="ArgumentException"/> /
    /// <see cref="InvalidOperationException"/> (already a member).
    /// </summary>
    Task<MembershipEntity> InviteMemberAsync(
        Guid accountId, string email, AccountRole role, Guid invitedByUserAccountId, CancellationToken ct = default);

    /// <summary>Changes a member's role. Returns false if not permitted (actor must outrank target and may grant the role).</summary>
    Task<bool> ChangeRoleAsync(Guid accountId, Guid targetUserAccountId, AccountRole newRole, Guid actorUserAccountId, CancellationToken ct = default);

    /// <summary>Removes a member. Returns false if not permitted (actor must outrank the target).</summary>
    Task<bool> RemoveMemberAsync(Guid accountId, Guid targetUserAccountId, Guid actorUserAccountId, CancellationToken ct = default);

    /// <summary>Lists the members of an account, newest first.</summary>
    Task<IReadOnlyList<MemberView>> ListMembersAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Lists the accounts a user belongs to, with their role in each.</summary>
    Task<IReadOnlyList<AccountSummary>> ListAccountsForUserAsync(Guid userAccountId, CancellationToken ct = default);

    /// <summary>The user's role in the account, or null if they aren't a member.</summary>
    Task<AccountRole?> GetRoleAsync(Guid accountId, Guid userAccountId, CancellationToken ct = default);
}
