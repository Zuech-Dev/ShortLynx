using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Repository;

/// <summary>
/// Shared by <see cref="EfCoreDbOperations"/> and <see cref="PostgresDbOperations"/> — see
/// <see cref="IDbOperations.UpsertCityClicksAsync"/> for why this one method isn't COPY-optimized like
/// the other three and doesn't need a provider-specific implementation.
/// </summary>
internal static class CityClickUpsert
{
    public static async Task RunAsync(ShortLynxDbContext db, IReadOnlyCollection<CityClickItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return;

        // Click counts per (link, city, country, date) -- the CityClickDailyEntity key.
        var byGroup = items
            .GroupBy(i => (i.LinkId, i.City, i.Country, i.Date))
            .ToList();

        // Distinct (link, city, country, date, hashedIp) touched by this batch, deduped within the
        // batch itself -- the same visitor can click the same link's short code twice in one flush.
        var candidates = items
            .Select(i => (i.LinkId, i.City, i.Country, i.Date, i.HashedIp))
            .Distinct()
            .ToList();

        // Which of those are genuinely new today vs. already recorded by an earlier flush this
        // rotation day. Scoped by LinkId+Date to keep the lookup narrow rather than scanning the whole
        // table.
        var linkIds = candidates.Select(c => c.LinkId).Distinct().ToList();
        var dates = candidates.Select(c => c.Date).Distinct().ToList();
        var existing = (await db.Set<CityClickDailyVisitorEntity>()
                .Where(v => linkIds.Contains(v.LinkId) && dates.Contains(v.Date))
                .Select(v => new { v.LinkId, v.City, v.Country, v.Date, v.HashedIp })
                .ToListAsync(ct))
            .Select(v => (v.LinkId, v.City, v.Country, v.Date, v.HashedIp))
            .ToHashSet();

        var newVisitors = candidates.Where(c => !existing.Contains(c)).ToList();
        if (newVisitors.Count > 0)
        {
            db.Set<CityClickDailyVisitorEntity>().AddRange(newVisitors.Select(v => new CityClickDailyVisitorEntity
            {
                Id = Guid.CreateVersion7(),
                LinkId = v.LinkId,
                City = v.City,
                Country = v.Country,
                Date = v.Date,
                HashedIp = v.HashedIp,
            }));
        }

        var newUniqueByGroup = newVisitors
            .GroupBy(v => (v.LinkId, v.City, v.Country, v.Date))
            .ToDictionary(g => g.Key, g => (long)g.Count());

        foreach (var group in byGroup)
        {
            var (linkId, city, country, date) = group.Key;
            var clickCount = group.LongCount();
            var newUnique = newUniqueByGroup.GetValueOrDefault(group.Key, 0);

            var daily = await db.Set<CityClickDailyEntity>()
                .FirstOrDefaultAsync(d => d.LinkId == linkId && d.City == city && d.Country == country && d.Date == date, ct);
            if (daily is null)
            {
                db.Set<CityClickDailyEntity>().Add(new CityClickDailyEntity
                {
                    Id = Guid.CreateVersion7(),
                    LinkId = linkId,
                    City = city,
                    Country = country,
                    Date = date,
                    Count = clickCount,
                    UniqueCount = newUnique,
                });
            }
            else
            {
                daily.Count += clickCount;
                daily.UniqueCount += newUnique;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
