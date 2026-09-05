using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Users;

public sealed class UserPreferencesService(ShortLynxDbContext db) : IUserPreferencesService
{
    public async Task<NavStyle?> GetNavStyleAsync(Guid userAccountId, CancellationToken ct = default) =>
        await db.UserAccountEntities
            .Where(u => u.Id == userAccountId)
            .Select(u => (NavStyle?)u.NavStyle)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> SetNavStyleAsync(Guid userAccountId, NavStyle style, CancellationToken ct = default) =>
        await db.UserAccountEntities
            .Where(u => u.Id == userAccountId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.NavStyle, style), ct) > 0;
}
