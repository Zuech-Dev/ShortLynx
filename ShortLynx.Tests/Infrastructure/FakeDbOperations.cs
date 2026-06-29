using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Tests.Infrastructure;

internal sealed class FakeDbOperations : IDbOperations
{
    private readonly object _lock = new();

    internal List<UserLinkCodeEntity> InsertedCodes { get; } = [];
    internal List<VisitEntity> InsertedVisits { get; } = [];
    internal List<UserVisitEntity> InsertedUserVisits { get; } = [];

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
}
