using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Enums;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;
using ShortLynx.Services.Analytics;

namespace ShortLynx.Services.Visits;

public sealed class BackgroundVisitWriter(
    InMemoryVisitEventSink sink,
    IServiceScopeFactory scopeFactory,
    IOptions<VisitSinkOptions> options,
    IUserAgentParser uaParser,
    IReferrerReducer referrerReducer,
    ILanguageReducer languageReducer,
    IGeoIpResolver geoIp) : BackgroundService
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
                await FlushAsync(batch, dbOps, _opts.IpHashPepper, stoppingToken);
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

    private async Task FlushAsync(List<VisitEvent> batch, IDbOperations dbOps, string pepper, CancellationToken ct)
    {
        var mode1 = batch
            .Where(e => e.ShortCodeId.HasValue)
            .Select(e =>
            {
                var d = Derive(e);
                var utm = ParseUtm(e);
                return new VisitEntity
                {
                    Id = Guid.CreateVersion7(),
                    ShortCodeId = e.ShortCodeId!.Value,
                    ClickedAt = e.ClickedAt,
                    HashedIp = HashIp(e.RawIp, pepper),
                    Source = d.Source,
                    Device = d.Device,
                    Browser = d.Browser,
                    Os = d.Os,
                    ReferrerHost = d.ReferrerHost,
                    Country = d.Country,
                    TimeZone = d.TimeZone,
                    Language = d.Language,
                    NavigationType = d.NavigationType,
                    UtmSource = utm.Source,
                    UtmMedium = utm.Medium,
                    UtmCampaign = utm.Campaign,
                    UtmTerm = utm.Term,
                    UtmContent = utm.Content,
                };
            })
            .ToList();

        var mode2 = batch
            .Where(e => e.UserLinkCodeId.HasValue)
            .Select(e =>
            {
                var d = Derive(e);
                var utm = ParseUtm(e);
                return new UserVisitEntity
                {
                    Id = Guid.CreateVersion7(),
                    UserLinkCodeId = e.UserLinkCodeId!.Value,
                    UserId = e.UserId,
                    ClickedAt = e.ClickedAt,
                    HashedIp = HashIp(e.RawIp, pepper),
                    Source = d.Source,
                    Device = d.Device,
                    Browser = d.Browser,
                    Os = d.Os,
                    ReferrerHost = d.ReferrerHost,
                    Country = d.Country,
                    TimeZone = d.TimeZone,
                    Language = d.Language,
                    NavigationType = d.NavigationType,
                    UtmSource = utm.Source,
                    UtmMedium = utm.Medium,
                    UtmCampaign = utm.Campaign,
                    UtmTerm = utm.Term,
                    UtmContent = utm.Content,
                };
            })
            .ToList();

        if (mode1.Count > 0) await dbOps.BulkInsertVisitsAsync(mode1, ct);
        if (mode2.Count > 0) await dbOps.BulkInsertUserVisitsAsync(mode2, ct);
    }

    // UTM tags ride the inbound query string; like every dimension they are suppressed under a
    // privacy signal, and the raw query is never persisted.
    private static UtmTags ParseUtm(VisitEvent e)
        => e.PrivacySignal ? UtmTags.Empty : UtmParser.Parse(e.RawQuery);

    // Reduces a visit's raw signals to the stored low-entropy dimensions. A privacy signal (DNT / Sec-GPC)
    // suppresses every derived dimension — the click still counts, but carries no profile.
    private (ClickSource Source, DeviceType Device, string? Browser, string? Os, string? ReferrerHost,
        string? Country, string? TimeZone, string? Language, string? NavigationType) Derive(VisitEvent e)
    {
        if (e.PrivacySignal)
            return (ClickSource.Direct, DeviceType.Unknown, null, null, null, null, null, null, null);

        var ua = uaParser.Parse(e.UserAgent);
        var nav = string.IsNullOrWhiteSpace(e.SecFetchSite) ? null : e.SecFetchSite.Trim().ToLowerInvariant();
        var geo = geoIp.Resolve(e.RawIp);
        return (
            SourceDetector.DetectSource(e.Referrer),
            ua.Device,
            ua.Browser,
            ua.Os,
            referrerReducer.Host(e.Referrer),
            geo.Country,
            geo.TimeZone,
            languageReducer.Primary(e.AcceptLanguage),
            nav);
    }

    // IP hashing is keyed with a secret pepper (HMAC) so the small IPv4 space can't be brute-forced
    // back to the original address, plus a rotating hourly component to limit cross-day linkage.
    internal static string HashIp(string rawIp, string pepper)
    {
        var hourly = $"{rawIp}:{DateTimeOffset.UtcNow:yyyyMMddHH}";
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(hourly);
        return Convert.ToHexString(HMACSHA256.HashData(key, data));
    }
}
