using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Visits;

/// <summary>
/// One click, flattened for the live feed. Deliberately a superset of what a chart needs and a subset
/// of <see cref="Data.Entities.VisitEntity"/>: no <c>HashedIp</c>, because the live feed is pushed to a
/// browser and the hash is pseudonymous-but-still-personal data with no use in a UI.
/// </summary>
public readonly record struct LiveVisitRow(
    Guid Id,
    Guid LinkId,
    string Code,
    Guid? UserId,
    DateTimeOffset ClickedAt,
    ClickSource Source,
    DeviceType Device,
    string? Browser,
    string? Os,
    string? Country,
    string? TimeZone,
    string? Language,
    string? ReferrerHost,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign);

/// <summary>
/// The read side of the live click feed: "which clicks landed on this account's links since T?".
///
/// This is a *tail*, not a subscription. Redirects are served by ShortLynx.Web, a different process
/// from the API a dashboard talks to, so an in-process <see cref="IVisitEventSink"/> fan-out can never
/// reach the browser. Polling the table the writer already commits to is the only mechanism that works
/// across that split without introducing a shared bus (Redis) as a hard dependency. If one is ever
/// added, this becomes the cold-start backfill and the bus takes over the steady state.
///
/// Two properties the caller depends on:
///
/// <list type="bullet">
/// <item><b>Ordered by ClickedAt ascending</b>, so a caller can advance a high-water mark.</item>
/// <item><b>Overlap-safe.</b> <c>ClickedAt</c> is stamped at redirect time but the row is written up to
///       a batch interval later (see <see cref="BackgroundVisitWriter"/>), so rows do NOT become visible
///       in <c>ClickedAt</c> order — a click can appear *behind* a high-water mark already advanced past
///       it. Callers must therefore re-query a window behind their cursor and de-duplicate on
///       <see cref="LiveVisitRow.Id"/>; querying strictly forward silently drops clicks.</item>
/// </list>
/// </summary>
public static class LiveVisitQueries
{
    /// <summary>
    /// Clicks on any of <paramref name="accountId"/>'s links with <c>ClickedAt &gt; since</c>, oldest
    /// first, capped at <paramref name="limit"/> rows.
    /// </summary>
    public static async Task<List<LiveVisitRow>> LoadSinceAsync(
        ShortLynxDbContext db,
        Guid accountId,
        DateTimeOffset since,
        int limit,
        CancellationToken ct = default)
    {
        var rows = new List<LiveVisitRow>();

        // Shared codes → Visits. Resolve code→link in one hop so the visit query stays a single
        // indexed IN (...) rather than a join across three tables.
        var shortCodes = await db.ShortCodeEntities
            .Where(sc => db.LinkEntities.Any(l => l.Id == sc.LinkId && l.AccountId == accountId))
            .Select(sc => new { sc.Id, sc.LinkId, sc.Code })
            .ToListAsync(ct);

        if (shortCodes.Count > 0)
        {
            var meta = shortCodes.ToDictionary(x => x.Id, x => (x.LinkId, x.Code));
            var ids = meta.Keys.ToList();

            var visits = await FilterByClickedAtAsync(
                db,
                db.VisitEntities.Where(v => v.ShortCodeId != null && ids.Contains(v.ShortCodeId.Value)),
                v => v.ClickedAt,
                v => new Projection(
                    v.Id, v.ShortCodeId!.Value, null, v.ClickedAt, v.Source, v.Device, v.Browser, v.Os,
                    v.Country, v.TimeZone, v.Language, v.ReferrerHost, v.UtmSource, v.UtmMedium, v.UtmCampaign),
                since, limit, ct);

            rows.AddRange(visits.Select(v => ToRow(v, meta[v.CodeId])));
        }

        // Per-recipient codes → UserVisits. Mode 2 clicks are the same fact, in a second table.
        var userCodes = await db.UserLinkCodeEntities
            .Where(uc => db.LinkEntities.Any(l => l.Id == uc.LinkId && l.AccountId == accountId))
            .Select(uc => new { uc.Id, uc.LinkId, uc.Code })
            .ToListAsync(ct);

        if (userCodes.Count > 0)
        {
            var meta = userCodes.ToDictionary(x => x.Id, x => (x.LinkId, x.Code));
            var ids = meta.Keys.ToList();

            var visits = await FilterByClickedAtAsync(
                db,
                db.UserVisitEntities.Where(v => ids.Contains(v.UserLinkCodeId)),
                v => v.ClickedAt,
                v => new Projection(
                    v.Id, v.UserLinkCodeId, v.UserId, v.ClickedAt, v.Source, v.Device, v.Browser, v.Os,
                    v.Country, v.TimeZone, v.Language, v.ReferrerHost, v.UtmSource, v.UtmMedium, v.UtmCampaign),
                since, limit, ct);

            rows.AddRange(visits.Select(v => ToRow(v, meta[v.CodeId])));
        }

        // Merge the two sources into one ordered stream. Sorting after the union (rather than relying
        // on either query's order) is what makes the caller's high-water mark meaningful.
        rows.Sort(static (a, b) => a.ClickedAt.CompareTo(b.ClickedAt));
        return rows.Count > limit ? rows.GetRange(0, limit) : rows;
    }

    private readonly record struct Projection(
        Guid Id, Guid CodeId, Guid? UserId, DateTimeOffset ClickedAt, ClickSource Source, DeviceType Device,
        string? Browser, string? Os, string? Country, string? TimeZone, string? Language, string? ReferrerHost,
        string? UtmSource, string? UtmMedium, string? UtmCampaign);

    private static LiveVisitRow ToRow(Projection p, (Guid LinkId, string Code) meta)
        => new(p.Id, meta.LinkId, meta.Code, p.UserId, p.ClickedAt, p.Source, p.Device, p.Browser,
               p.Os, p.Country, p.TimeZone, p.Language, p.ReferrerHost, p.UtmSource, p.UtmMedium, p.UtmCampaign);

    /// <summary>
    /// Applies the <c>ClickedAt &gt; since</c> cut. SQLite cannot translate a <c>DateTimeOffset</c>
    /// comparison to SQL (the same limitation <c>VisitRetentionService</c> and <c>MagicLinkService</c>
    /// work around), so the filter runs client-side there, pulling the (already narrow) projection into
    /// memory first. On PostgreSQL the filter and ordering are pushed to the database — but against the
    /// entity's own <c>ClickedAt</c> column via <paramref name="clickedAt"/>, not against the projected
    /// <see cref="Projection"/> record: filtering by a property read off an already-constructed record
    /// (i.e. <c>.Select(project).Where(p =&gt; p.ClickedAt &gt; since)</c>) does not translate — EF
    /// cannot push a comparison against a freshly-materialized record's property back down into SQL —
    /// and throws <see cref="InvalidOperationException"/> at query time, not at compile time, so this
    /// only surfaces against a real relational provider under an actual request, never against the
    /// in-memory/SQLite path the test suite runs on.
    /// </summary>
    private static async Task<List<Projection>> FilterByClickedAtAsync<TEntity>(
        ShortLynxDbContext db,
        IQueryable<TEntity> source,
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> clickedAt,
        System.Linq.Expressions.Expression<Func<TEntity, Projection>> project,
        DateTimeOffset since,
        int limit,
        CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var all = await source.Select(project).ToListAsync(ct);
            return all.Where(p => p.ClickedAt > since)
                      .OrderBy(p => p.ClickedAt)
                      .Take(limit)
                      .ToList();
        }

        return await source
            .Where(SinceFilter(clickedAt, since))
            .OrderBy(clickedAt)
            .Take(limit)
            .Select(project)
            .ToListAsync(ct);
    }

    /// <summary>Builds <c>entity =&gt; clickedAt(entity) &gt; since</c> as one expression tree, so the
    /// comparison reaches the provider as a plain column predicate rather than a call to
    /// <paramref name="clickedAt"/> the translator would have to inline itself.</summary>
    private static System.Linq.Expressions.Expression<Func<TEntity, bool>> SinceFilter<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, DateTimeOffset>> clickedAt, DateTimeOffset since)
    {
        var body = System.Linq.Expressions.Expression.GreaterThan(
            clickedAt.Body, System.Linq.Expressions.Expression.Constant(since));
        return System.Linq.Expressions.Expression.Lambda<Func<TEntity, bool>>(body, clickedAt.Parameters[0]);
    }
}
