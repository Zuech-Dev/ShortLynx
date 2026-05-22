using ShortLynx.Data.Entities;

namespace ShortLynx.Data.Operations;

public interface IDbOperations
{
    Task BulkInsertUserLinkCodesAsync(
        IEnumerable<UserLinkCodeEntity> codes, CancellationToken ct = default);

    Task BulkInsertVisitsAsync(
        IEnumerable<VisitEntity> visits, CancellationToken ct = default);

    Task BulkInsertUserVisitsAsync(
        IEnumerable<UserVisitEntity> visits, CancellationToken ct = default);
}
