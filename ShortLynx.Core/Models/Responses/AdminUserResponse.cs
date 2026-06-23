namespace ShortLynx.Core.Models.Responses;

/// <summary>A user as returned by the super-admin /admin/users surface, with their account memberships.</summary>
public sealed record AdminUserResponse(
    Guid Id, string Email, bool IsActive, bool IsAdmin, DateTimeOffset CreatedAt, AccountResponse[] Accounts);
