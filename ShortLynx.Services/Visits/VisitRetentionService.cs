using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.Visits;

/// <summary>
/// Nightly prune of visit rows older than the configured retention window, plus the two city-click-
/// aggregate tables (CITY_GEO_PLAN.md), which are pruned on a fixed schedule regardless of the
/// operator's VisitSink:AnalyticsRetentionDays setting — see the constants below for why. Self-hosters
/// set AnalyticsRetentionDays directly (null — the default — keeps raw visits forever); the hosted SaaS
/// will drive it per plan tier later. Deleting old rows is a privacy feature as much as a storage one:
/// data that no longer exists can't leak.
/// </summary>
public sealed class VisitRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<VisitSinkOptions> options,
    ILogger<VisitRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>CityClickDailyEntity is an aggregate count with no path back to an individual, but it
    /// still ages badly in a political-inference context (CITY_GEO_PLAN.md §6.2) — 90 days per that
    /// plan's own proposal, not operator-configurable.</summary>
    private const int CityClickDailyRetentionDays = 90;

    /// <summary>CityClickDailyVisitorEntity is the more sensitive of the two tables (a per-city set of
    /// hashed IPs) and is only ever needed for the day it's written on, to dedupe that day's uniques —
    /// once a date's count is finalized in CityClickDailyEntity, the presence rows have done their job.
    /// 2 days of buffer past that covers a delayed/retried flush without holding the higher-sensitivity
    /// table anywhere near as long as the aggregate it feeds.</summary>
    private const int CityClickVisitorRetentionDays = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

                if (options.Value.AnalyticsRetentionDays is { } days)
                {
                    var removed = await PruneOnceAsync(db, DateTimeOffset.UtcNow.AddDays(-days), stoppingToken);
                    if (removed > 0)
                        logger.LogInformation("Visit retention: pruned {Count} rows older than {Days} days", removed, days);
                }

                var cityRemoved = await PruneCityAggregatesOnceAsync(
                    db,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-CityClickDailyRetentionDays)),
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-CityClickVisitorRetentionDays)),
                    stoppingToken);
                if (cityRemoved > 0)
                    logger.LogInformation("Visit retention: pruned {Count} city-aggregate rows", cityRemoved);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let a prune failure take the host down; try again next cycle.
                logger.LogError(ex, "Visit retention prune failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Deletes CityClickDailyEntity rows older than <paramref name="dailyCutoff"/> and
    /// CityClickDailyVisitorEntity rows older than <paramref name="visitorCutoff"/> (always the more
    /// recent of the two cutoffs, since the visitor table is pruned far more aggressively). Both are
    /// plain equality/range comparisons on a DateOnly column, so — unlike PruneOnceAsync's
    /// DateTimeOffset comparisons — there's no SQLite-vs-PostgreSQL branch needed here.</summary>
    public static async Task<int> PruneCityAggregatesOnceAsync(
        ShortLynxDbContext db, DateOnly dailyCutoff, DateOnly visitorCutoff, CancellationToken ct = default)
    {
        var daily = await db.CityClickDailyEntities.Where(c => c.Date < dailyCutoff).ExecuteDeleteAsync(ct);
        var visitors = await db.CityClickDailyVisitorEntities.Where(v => v.Date < visitorCutoff).ExecuteDeleteAsync(ct);
        return daily + visitors;
    }

    /// <summary>Deletes visits (both modes) clicked before <paramref name="cutoff"/>. Set-based
    /// deletes, no entity tracking — safe for large tables. Exposed for tests.</summary>
    public static async Task<int> PruneOnceAsync(ShortLynxDbContext db, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        // SQLite (dev/tests) can't compare DateTimeOffset in SQL, so resolve the doomed ids
        // client-side there; PostgreSQL takes the single-statement fast path.
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var visitIds = (await db.VisitEntities.Select(v => new { v.Id, v.ClickedAt }).ToListAsync(ct))
                .Where(v => v.ClickedAt < cutoff).Select(v => v.Id).ToList();
            var userVisitIds = (await db.UserVisitEntities.Select(v => new { v.Id, v.ClickedAt }).ToListAsync(ct))
                .Where(v => v.ClickedAt < cutoff).Select(v => v.Id).ToList();
            return await db.VisitEntities.Where(v => visitIds.Contains(v.Id)).ExecuteDeleteAsync(ct)
                 + await db.UserVisitEntities.Where(v => userVisitIds.Contains(v.Id)).ExecuteDeleteAsync(ct);
        }

        var visits = await db.VisitEntities.Where(v => v.ClickedAt < cutoff).ExecuteDeleteAsync(ct);
        var userVisits = await db.UserVisitEntities.Where(v => v.ClickedAt < cutoff).ExecuteDeleteAsync(ct);
        return visits + userVisits;
    }
}
