using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;

namespace ShortLynx.Services.Visits;

/// <summary>
/// Nightly prune of visit rows older than the configured retention window. Self-hosters set
/// VisitSink:AnalyticsRetentionDays directly (null — the default — keeps everything forever); the
/// hosted SaaS will drive this per plan tier later. Deleting old rows is a privacy feature as much as
/// a storage one: data that no longer exists can't leak.
/// </summary>
public sealed class VisitRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<VisitSinkOptions> options,
    ILogger<VisitRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.AnalyticsRetentionDays is not { } days)
            return; // retention disabled — keep everything

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
                var removed = await PruneOnceAsync(db, DateTimeOffset.UtcNow.AddDays(-days), stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Visit retention: pruned {Count} rows older than {Days} days", removed, days);
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
