using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>
/// Add a user. With <see cref="AccountId"/> set, the user is added to that existing account at
/// <see cref="Role"/> (default Member). Without it, a new account (<see cref="AccountName"/> or the
/// email) is created with the user as Owner.
/// </summary>
public sealed record AdminAddUserRequest(
    [Required, EmailAddress] string Email,
    Guid? AccountId = null,
    string? Role = null,
    string? AccountName = null);

/// <summary>Edit platform-level flags on a user. Omitted (null) fields are left unchanged.</summary>
public sealed record AdminEditUserRequest(bool? IsActive = null, bool? IsAdmin = null);

/// <summary>Assign a user to an existing account at a role (creates or changes the membership).</summary>
public sealed record AdminAssignAccountRequest([Required] string Role);
