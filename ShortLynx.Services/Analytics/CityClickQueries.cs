using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.Analytics;

/// <summary>One link's city-aggregate row for one calendar date — see CityClickDailyEntity. City can
/// be null (MaxMind resolved Country/State but not a specific city — common for mobile/business IP
/// blocks); CityAggregator.Summarize's cascade is what turns that into a State- or Country-level
/// reveal instead of dropping it.
///
/// A plain record, not a record struct: projecting a nullable column straight into a readonly
/// struct's constructor is a far less-traveled EF Core/Npgsql code path than the same projection into
/// a class, and it broke exactly there in production (System.InvalidCastException: Column 'City' is
/// null) — SQLite, what the test suite runs against, never surfaced it. City was never actually null
/// under the old schema, so this was latent until the write-gate fix made a genuine null possible.</summary>
public sealed record CityDailyRow(string? City, string? State, string? Country, long Count, long UniqueCount);

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
            .Select(c => new CityDailyRow(c.City, c.State, c.Country, c.Count, c.UniqueCount))
            .ToListAsync(ct);
    }
}
