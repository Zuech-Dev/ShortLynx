using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Tests.Infrastructure;

internal sealed class FakeDbOperations : IDbOperations
{
    internal List<UserLinkCodeEntity> InsertedCodes { get; } = [];
    internal List<VisitEntity> InsertedVisits { get; } = [];
    internal List<UserVisitEntity> InsertedUserVisits { get; } = [];

    public Task BulkInsertUserLinkCodesAsync(IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default)
    {
        InsertedCodes.AddRange(codes);
        return Task.CompletedTask;
    }

    public Task BulkInsertVisitsAsync(IEnumerable<VisitEntity> visits, CancellationToken ct = default)
    {
        InsertedVisits.AddRange(visits);
        return Task.CompletedTask;
    }

    public Task BulkInsertUserVisitsAsync(IEnumerable<UserVisitEntity> visits, CancellationToken ct = default)
    {
        InsertedUserVisits.AddRange(visits);
        return Task.CompletedTask;
    }
}
