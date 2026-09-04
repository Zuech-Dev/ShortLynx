using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
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
                // Same scope as dbOps, so this is the identical ShortLynxDbContext instance
                // EfCoreDbOperations/PostgresDbOperations already hold -- no extra connection, and
                // change tracking stays consistent between the eligibility read and the later writes.
                var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
                await FlushAsync(batch, dbOps, db, _opts.IpHashPepper, stoppingToken);
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

    private async Task FlushAsync(List<VisitEvent> batch, IDbOperations dbOps, ShortLynxDbContext db, string pepper, CancellationToken ct)
    {
        // City aggregates (CITY_GEO_PLAN.md) are per-account opt-in, so most flushes touch zero
        // eligible links -- both dictionaries come back empty and every per-event lookup below is a
        // cheap miss. Mode 2 is not resolved here at all: it's permanently excluded regardless of the
        // account's setting (a named recipient + city + a political/sensitive destination is a
        // categorically different risk than an anonymous aggregate).
        var eligibility = await ResolveCityEligibilityAsync(db, batch, ct);
        var cityItems = new List<CityClickItem>();

        // Both a shared-code click and a post-code click are the same fact and share the Visits row
        // shape — only which FK is set differs, so they batch together into one bulk insert.
        var mode1 = batch
            .Where(e => e.ShortCodeId.HasValue || e.SocialPostCodeId.HasValue)
            .Select(e =>
            {
                var linkId = e.ShortCodeId.HasValue
                    ? eligibility.ByShortCode.GetValueOrDefault(e.ShortCodeId.Value)
                    : eligibility.BySocialPostCode.GetValueOrDefault(e.SocialPostCodeId!.Value);
                var cityEligible = linkId != Guid.Empty;

                var d = Derive(e, cityEligible);
                var utm = ParseUtm(e);
                var hashedIp = HashIp(e.RawIp, pepper);

                // Bots are excluded the same way ClickAggregator excludes them from human-engagement
                // stats: they'd otherwise inflate small-city buckets past the k threshold with noise,
                // and a bot's "location" isn't a real visitor's location to reveal. Privacy-signal rows
                // are already excluded implicitly -- Derive returns City = null for them below.
                if (cityEligible && d.City is not null && d.Device != DeviceType.Bot)
                {
                    cityItems.Add(new CityClickItem(
                        linkId, d.City, d.Country, DateOnly.FromDateTime(e.ClickedAt.UtcDateTime), hashedIp));
                }

                return new VisitEntity
                {
                    Id = Guid.CreateVersion7(),
                    ShortCodeId = e.ShortCodeId,
                    SocialPostCodeId = e.SocialPostCodeId,
                    ClickedAt = e.ClickedAt,
                    HashedIp = hashedIp,
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
                var d = Derive(e); // includeCity defaults false -- Mode 2 never resolves city, see above
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
        if (cityItems.Count > 0) await dbOps.UpsertCityClicksAsync(cityItems, ct);
    }

    /// <summary>Which of this batch's ShortCodeIds/SocialPostCodeIds belong to a link whose account has
    /// EnableCityAggregates on, mapped to that LinkId. Absent from the dictionary == not eligible
    /// (opted out, or the account setting is simply off, which is the default for everyone).</summary>
    private static async Task<(Dictionary<Guid, Guid> ByShortCode, Dictionary<Guid, Guid> BySocialPostCode)>
        ResolveCityEligibilityAsync(ShortLynxDbContext db, List<VisitEvent> batch, CancellationToken ct)
    {
        var shortCodeIds = batch.Where(e => e.ShortCodeId.HasValue).Select(e => e.ShortCodeId!.Value).Distinct().ToList();
        var socialPostCodeIds = batch.Where(e => e.SocialPostCodeId.HasValue).Select(e => e.SocialPostCodeId!.Value).Distinct().ToList();

        var byShortCode = shortCodeIds.Count == 0
            ? []
            : await db.ShortCodeEntities
                .Where(sc => shortCodeIds.Contains(sc.Id) && sc.Link.Account.EnableCityAggregates)
                .Select(sc => new { sc.Id, sc.LinkId })
                .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        var bySocialPostCode = socialPostCodeIds.Count == 0
            ? []
            : await db.SocialPostCodeEntities
                .Where(spc => socialPostCodeIds.Contains(spc.Id) && spc.Link.Account.EnableCityAggregates)
                .Select(spc => new { spc.Id, spc.LinkId })
                .ToDictionaryAsync(x => x.Id, x => x.LinkId, ct);

        return (byShortCode, bySocialPostCode);
    }

    // UTM tags ride the inbound query string; like every dimension they are suppressed under a
    // privacy signal, and the raw query is never persisted.
    private static UtmTags ParseUtm(VisitEvent e)
        => e.PrivacySignal ? UtmTags.Empty : UtmParser.Parse(e.RawQuery);

    // Reduces a visit's raw signals to the stored low-entropy dimensions. A privacy signal (DNT / Sec-GPC)
    // suppresses every derived dimension — the click still counts, but carries no profile. includeCity
    // must only ever be true for a Mode 1 event whose account has opted into city aggregates (resolved
    // by ResolveCityEligibilityAsync); City here never reaches VisitEntity/UserVisitEntity itself --
    // callers collect it into a CityClickItem instead. See MaxMindGeoIpResolver for why this is safe to
    // ask for unconditionally rather than gating inside the resolver.
    private (ClickSource Source, DeviceType Device, string? Browser, string? Os, string? ReferrerHost,
        string? Country, string? TimeZone, string? Language, string? NavigationType, string? City) Derive(VisitEvent e, bool includeCity = false)
    {
        if (e.PrivacySignal)
            return (ClickSource.Direct, DeviceType.Unknown, null, null, null, null, null, null, null, null);

        var ua = uaParser.Parse(e.UserAgent);
        var nav = string.IsNullOrWhiteSpace(e.SecFetchSite) ? null : e.SecFetchSite.Trim().ToLowerInvariant();
        var geo = geoIp.Resolve(e.RawIp, includeCity);
        return (
            SourceDetector.DetectSource(e.Referrer),
            ua.Device,
            ua.Browser,
            ua.Os,
            referrerReducer.Host(e.Referrer),
            geo.Country,
            geo.TimeZone,
            languageReducer.Primary(e.AcceptLanguage),
            nav,
            geo.City);
    }

    // IP hashing is keyed with a secret pepper (HMAC) so the small IPv4 space can't be brute-forced
    // back to the original address, plus a daily rotating component (see DailyBucket) that limits how
    // long the same visitor's hash stays linkable, while keeping a single day's traffic analyzable as
    // one consistent bucket rather than splintering it across 24 hourly ones.
    internal static string HashIp(string rawIp, string pepper) => HashIp(rawIp, pepper, DateTimeOffset.UtcNow);

    internal static string HashIp(string rawIp, string pepper, DateTimeOffset now)
    {
        var bucketed = $"{rawIp}:{DailyBucket(now)}";
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(bucketed);
        return Convert.ToHexString(HMACSHA256.HashData(key, data));
    }

    // Resolved once — TimeZoneInfo lookup isn't free, and this runs on the hot redirect path.
    private static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>
    /// The hash-rotation bucket: one value per calendar day in US Eastern time, with the boundary at
    /// 5am ET rather than midnight. "America/New_York" (not a fixed UTC offset) means the boundary
    /// stays 5am local through the EST/EDT transition automatically. 5am is deliberately the least
    /// active hour for this product's traffic rather than midnight: anchoring the rotation to a quiet
    /// hour makes it very unlikely a single browsing session ever straddles two buckets, which a
    /// midnight boundary would risk for evening traffic.
    /// </summary>
    internal static string DailyBucket(DateTimeOffset utcNow)
    {
        var eastern = TimeZoneInfo.ConvertTime(utcNow, EasternTimeZone).DateTime;
        var bucketDate = eastern.Hour < 5 ? eastern.Date.AddDays(-1) : eastern.Date;
        return bucketDate.ToString("yyyyMMdd");
    }
}
