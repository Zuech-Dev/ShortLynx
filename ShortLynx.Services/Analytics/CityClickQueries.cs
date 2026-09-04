using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.Analytics;

/// <summary>One link's city-aggregate row for one calendar date — see CityClickDailyEntity.</summary>
public readonly record struct CityDailyRow(string City, string? Country, long Count, long UniqueCount);

public static class CityClickQueries
{
    /// <summary>
    /// Every CityClickDailyEntity row for the given links, unaggregated across dates (that's
    /// CityAggregator.Summarize's job). Empty for links whose account never had EnableCityAggregates on
    /// — most links, since it's opt-in and off by default. No date filter, matching
    /// LinkVisitQueries.LoadLinkRowsAsync's all-time convention for the rest of a link's analytics.
    /// </summary>
    public static async Task<List<CityDailyRow>> LoadForLinksAsync(
        ShortLynxDbContext db, IReadOnlyCollection<Guid> linkIds, CancellationToken ct = default)
    {
        if (linkIds.Count == 0) return [];
        return await db.CityClickDailyEntities
            .Where(c => linkIds.Contains(c.LinkId))
            .Select(c => new CityDailyRow(c.City, c.Country, c.Count, c.UniqueCount))
            .ToListAsync(ct);
    }
}
