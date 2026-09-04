using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Tests.Infrastructure;

internal sealed class FakeDbOperations : IDbOperations
{
    private readonly object _lock = new();

    internal List<UserLinkCodeEntity> InsertedCodes { get; } = [];
    internal List<VisitEntity> InsertedVisits { get; } = [];
    internal List<UserVisitEntity> InsertedUserVisits { get; } = [];
    // Raw items as passed, not the grouped/counted result -- this fake exists to let BackgroundVisitWriter
    // tests assert on *which* events it decided were eligible (the filtering logic actually lives there);
    // the upsert/grouping arithmetic itself is tested separately against a real DbContext (CityClickUpsertTests).
    internal List<CityClickItem> UpsertedCityClicks { get; } = [];

    // Thread-safe counters for tests that poll while the background writer flushes on another thread.
    // (The list properties above stay readable directly once the writer has stopped.)
    internal int VisitCount { get { lock (_lock) return InsertedVisits.Count; } }
    internal int UserVisitCount { get { lock (_lock) return InsertedUserVisits.Count; } }

    public Task BulkInsertUserLinkCodesAsync(IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default)
    {
        lock (_lock) InsertedCodes.AddRange(codes);
        return Task.CompletedTask;
    }

    public Task BulkInsertVisitsAsync(IEnumerable<VisitEntity> visits, CancellationToken ct = default)
    {
        lock (_lock) InsertedVisits.AddRange(visits);
        return Task.CompletedTask;
    }

    public Task BulkInsertUserVisitsAsync(IEnumerable<UserVisitEntity> visits, CancellationToken ct = default)
    {
        lock (_lock) InsertedUserVisits.AddRange(visits);
        return Task.CompletedTask;
    }

    public Task UpsertCityClicksAsync(IReadOnlyCollection<CityClickItem> items, CancellationToken ct = default)
    {
        lock (_lock) UpsertedCityClicks.AddRange(items);
        return Task.CompletedTask;
    }
}
