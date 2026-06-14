using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;

namespace ShortLynx.Services.Visits;

public sealed class BackgroundVisitWriter(
    InMemoryVisitEventSink sink,
    IServiceScopeFactory scopeFactory,
    IOptions<VisitSinkOptions> options) : BackgroundService
{
    private readonly VisitSinkOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await CollectBatchAsync(stoppingToken);
            if (batch.Count > 0)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbOps = scope.ServiceProvider.GetRequiredService<IDbOperations>();
                await FlushAsync(batch, dbOps, stoppingToken);
            }
        }
    }

    private async Task<List<VisitEvent>> CollectBatchAsync(CancellationToken stoppingToken)
    {
        var batch = new List<VisitEvent>(_opts.BatchSize);

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        drainCts.CancelAfter(_opts.DrainIntervalMs);

        try
        {
            while (batch.Count < _opts.BatchSize)
            {
                if (!await sink.Reader.WaitToReadAsync(drainCts.Token))
                    break;
                while (batch.Count < _opts.BatchSize && sink.Reader.TryRead(out var evt))
                    batch.Add(evt);
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Drain interval elapsed — process what we have.
        }

        return batch;
    }

    private static async Task FlushAsync(List<VisitEvent> batch, IDbOperations dbOps, CancellationToken ct)
    {
        var mode1 = batch
            .Where(e => e.ShortCodeId.HasValue)
            .Select(e => new VisitEntity
            {
                Id = Guid.CreateVersion7(),
                ShortCodeId = e.ShortCodeId!.Value,
                ClickedAt = e.ClickedAt,
                HashedIp = HashIp(e.RawIp),
                Referrer = e.Referrer,
                UserAgent = e.UserAgent,
            })
            .ToList();

        var mode2 = batch
            .Where(e => e.UserLinkCodeId.HasValue)
            .Select(e => new UserVisitEntity
            {
                Id = Guid.CreateVersion7(),
                UserLinkCodeId = e.UserLinkCodeId!.Value,
                UserId = e.UserId,
                ClickedAt = e.ClickedAt,
                HashedIp = HashIp(e.RawIp),
                Referrer = e.Referrer,
                UserAgent = e.UserAgent,
            })
            .ToList();

        if (mode1.Count > 0) await dbOps.BulkInsertVisitsAsync(mode1, ct);
        if (mode2.Count > 0) await dbOps.BulkInsertUserVisitsAsync(mode2, ct);
    }

    // IP hashing uses a rotating hourly salt to limit the window in which two
    // records from the same IP can be linked across days.
    internal static string HashIp(string rawIp)
    {
        var salt = $"salt-{DateTimeOffset.UtcNow:yyyyMMddHH}";
        var input = Encoding.UTF8.GetBytes($"{rawIp}:{salt}");
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
