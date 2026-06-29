using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Services.Users;

/// <summary>A user as seen by a platform super-admin, with every account they belong to.</summary>
public sealed record AdminUserView(
    Guid Id, string Email, bool IsActive, bool IsAdmin, DateTimeOffset CreatedAt,
    IReadOnlyList<AccountSummary> Accounts);

/// <summary>
/// Platform-level (super-admin) user management. Unlike <see cref="IAccountService"/>, whose writes are
/// gated by the actor's role <em>within an account</em>, these operate across tenants and must be gated
/// by super-admin authorization at the call site.
/// </summary>
public interface IUserAdminService
{
    /// <summary>Lists users (newest first) with their account memberships.</summary>
    Task<IReadOnlyList<AdminUserView>> ListUsersAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Adds a user. When <paramref name="accountId"/> is supplied it must reference an existing account
    /// (else <see cref="KeyNotFoundException"/>) and the user is added to it at <paramref name="role"/>
    /// (default <see cref="AccountRole.Member"/>). When it is null the current behavior applies: a new
    /// account (named <paramref name="newAccountName"/> or the email) is created with the user as Owner.
    /// Creates the user record if the email is new and emails them a sign-in link.
    /// </summary>
    Task<AdminUserView> AddUserAsync(
        string email, Guid? accountId = null, AccountRole? role = null, string? newAccountName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Assigns the user to an existing account at <paramref name="role"/>, creating the membership or
    /// changing the role if one already exists. The account must exist (<see cref="KeyNotFoundException"/>);
    /// returns false if the user doesn't exist.
    /// </summary>
    Task<bool> AssignToAccountAsync(Guid userId, Guid accountId, AccountRole role, CancellationToken ct = default);

    /// <summary>Removes the user's membership in an account. Returns false if no such membership.</summary>
    Task<bool> RemoveFromAccountAsync(Guid userId, Guid accountId, CancellationToken ct = default);

    /// <summary>Toggles the platform super-admin flag. Returns false if the user doesn't exist.</summary>
    Task<bool> SetSuperAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// Enables/disables the user. Disabling (soft delete) blocks sign-in while preserving the record and
    /// its memberships. Returns false if the user doesn't exist.
    /// </summary>
    Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct = default);
}
