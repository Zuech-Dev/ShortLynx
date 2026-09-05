namespace ShortLynx.Core.Models.Responses;

/// <summary>A user as returned by the super-admin /admin/users surface, with their account memberships.</summary>
public sealed record AdminUserResponse(
    Guid Id, string Email, bool IsActive, bool IsAdmin, DateTimeOffset CreatedAt, AccountResponse[] Accounts);

/// <summary>
/// One row of the super-admin /admin/accounts list — just enough to populate an account picker
/// (assigning a user to an existing account). Full settings are <see cref="AccountSettingsResponse"/>,
/// fetched per-account via <c>GET /admin/accounts/{id}</c>.
/// </summary>
public sealed record AdminAccountSummaryResponse(Guid Id, string Name);
