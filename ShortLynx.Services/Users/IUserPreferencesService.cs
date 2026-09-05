using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Users;

public interface IUserPreferencesService
{
    /// <summary>The user's nav style preference, or null if the user doesn't exist.</summary>
    Task<NavStyle?> GetNavStyleAsync(Guid userAccountId, CancellationToken ct = default);

    /// <summary>Sets the user's nav style preference. Returns false if the user doesn't exist.</summary>
    Task<bool> SetNavStyleAsync(Guid userAccountId, NavStyle style, CancellationToken ct = default);
}
