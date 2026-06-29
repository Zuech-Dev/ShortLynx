using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Repository;

public class EfCoreDbOperations(ShortLynxDbContext db) : IDbOperations
{
    public async Task BulkInsertUserLinkCodesAsync(IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default)
    {
        db.Set<UserLinkCodeEntity>().AddRange(codes);
        await db.SaveChangesAsync(ct);
    }

    public async Task BulkInsertVisitsAsync(IEnumerable<VisitEntity> visits, CancellationToken ct = default)
    {
        db.Set<VisitEntity>().AddRange(visits);
        await db.SaveChangesAsync(ct);
    }

    public async Task BulkInsertUserVisitsAsync(IEnumerable<UserVisitEntity> visits, CancellationToken ct = default)
    {
        db.Set<UserVisitEntity>().AddRange(visits);
        await db.SaveChangesAsync(ct);
    }
}