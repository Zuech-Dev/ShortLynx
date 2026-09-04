using ShortLynx.Data.Entities;

namespace ShortLynx.Data.Operations;

/// <summary>One eligible click, pre-filtered by the caller (not a privacy signal, not a bot, Mode 1,
/// account has EnableCityAggregates on) — see CityClickDailyEntity/CityClickDailyVisitorEntity.</summary>
public sealed record CityClickItem(Guid LinkId, string City, string? Country, DateOnly Date, string HashedIp);

public interface IDbOperations
{
    Task BulkInsertUserLinkCodesAsync(
        IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default);

    Task BulkInsertVisitsAsync(
        IEnumerable<VisitEntity> visits, CancellationToken ct = default);

    Task BulkInsertUserVisitsAsync(
        IEnumerable<UserVisitEntity> visits, CancellationToken ct = default);

    /// <summary>Upserts CityClickDailyEntity (Count always increments; UniqueCount increments only for
    /// hashed IPs not already seen for that link/city/date, tracked via CityClickDailyVisitorEntity).
    /// Low volume and opt-in only, so this doesn't need the COPY-based optimization the other three
    /// methods use — a plain EF Core implementation is shared by both <c>EfCoreDbOperations</c> and
    /// <c>PostgresDbOperations</c> rather than duplicated.</summary>
    Task UpsertCityClicksAsync(IReadOnlyCollection<CityClickItem> items, CancellationToken ct = default);
}
