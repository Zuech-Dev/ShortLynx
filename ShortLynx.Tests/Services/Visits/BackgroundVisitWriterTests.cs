using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Services.Visits;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Visits;

public class BackgroundVisitWriterTests
{
    private static async Task<(InMemoryVisitEventSink Sink, FakeDbOperations Db, BackgroundVisitWriter Writer, TestDatabase TestDb)>
        MakeWriter(int drainMs = 20, int batchSize = 100)
    {
        var opts = Options.Create(new VisitSinkOptions
        {
            ChannelCapacity = 1_000,
            BatchSize = batchSize,
            DrainIntervalMs = drainMs,
        });
        var db = new FakeDbOperations();

        // The writer now also resolves a ShortLynxDbContext per flush (city-aggregate eligibility —
        // see ResolveCityEligibilityAsync), even though these tests never seed an account with
        // EnableCityAggregates on. An empty-but-real test database, not a missing registration, keeps
        // that resolution a real (zero-row) query instead of a DI failure that silently stalls the
        // writer -- exactly what broke here the first time this was wired in.
        var testDb = await TestDatabase.CreateAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IDbOperations>(db);
        services.AddScoped(_ => testDb.CreateContext());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var sink = new InMemoryVisitEventSink(opts);
        var writer = new BackgroundVisitWriter(sink, scopeFactory, opts,
            new ShortLynx.Services.Analytics.UserAgentParser(),
            new ShortLynx.Services.Analytics.ReferrerReducer(),
            new ShortLynx.Services.Analytics.LanguageReducer(),
            new StubGeoIpResolver());
        return (sink, db, writer, testDb);
    }

    // Wait for the background writer to flush the expected rows instead of guessing a fixed delay,
    // which flakes on slow/contended CI runners. Falls through on timeout so the assertion reports the
    // real shortfall. Reads go through FakeDbOperations' locked counters (writer flushes on another thread).
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(15);
    }

    private static VisitEvent Mode1Event(string ip = "1.2.3.4") => new(
        ShortCodeId: Guid.CreateVersion7(),
        UserLinkCodeId: null,
        UserId: null,
        SocialPostCodeId: null,
        RawIp: ip,
        Referrer: "https://referrer.example",
        UserAgent: "test-agent",
        ClickedAt: DateTimeOffset.UtcNow);

    private static VisitEvent Mode2Event(string ip = "1.2.3.4")
    {
        var userId = Guid.CreateVersion7();
        return new VisitEvent(
            ShortCodeId: null,
            UserLinkCodeId: Guid.CreateVersion7(),
            UserId: userId,
            SocialPostCodeId: null,
            RawIp: ip,
            Referrer: null,
            UserAgent: null,
            ClickedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Writer_RoutesMode1Events_To_BulkInsertVisits()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        for (var i = 0; i < 3; i++) await sink.EnqueueAsync(Mode1Event());
        await WaitUntilAsync(() => db.VisitCount >= 3);
        await cts.CancelAsync();

        Assert.Equal(3, db.InsertedVisits.Count);
        Assert.Empty(db.InsertedUserVisits);
    }

    [Fact]
    public async Task Writer_RoutesMode2Events_To_BulkInsertUserVisits()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        for (var i = 0; i < 4; i++) await sink.EnqueueAsync(Mode2Event());
        await WaitUntilAsync(() => db.UserVisitCount >= 4);
        await cts.CancelAsync();

        Assert.Empty(db.InsertedVisits);
        Assert.Equal(4, db.InsertedUserVisits.Count);
    }

    [Fact]
    public async Task Writer_HandlesMixedModeEvents()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event());
        await sink.EnqueueAsync(Mode2Event());
        await sink.EnqueueAsync(Mode1Event());
        await WaitUntilAsync(() => db.VisitCount >= 2 && db.UserVisitCount >= 1);
        await cts.CancelAsync();

        Assert.Equal(2, db.InsertedVisits.Count);
        Assert.Single(db.InsertedUserVisits);
    }

    [Fact]
    public async Task Writer_HashesIp_DoesNotStoreRawIp()
    {
        const string rawIp = "203.0.113.42";
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event(rawIp));
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        var stored = db.InsertedVisits.Single();
        Assert.NotEqual(rawIp, stored.HashedIp);
        Assert.Equal(64, stored.HashedIp.Length); // SHA256 → 32 bytes → 64 hex chars
    }

    [Fact]
    public async Task Writer_SameIpSameRotationDay_ProducesSameHash()
    {
        const string rawIp = "203.0.113.1";
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event(rawIp));
        await sink.EnqueueAsync(Mode1Event(rawIp));
        await WaitUntilAsync(() => db.VisitCount >= 2);
        await cts.CancelAsync();

        Assert.Equal(db.InsertedVisits[0].HashedIp, db.InsertedVisits[1].HashedIp);
    }

    [Fact]
    public async Task Writer_DifferentIps_ProduceDifferentHashes()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event("1.1.1.1"));
        await sink.EnqueueAsync(Mode1Event("2.2.2.2"));
        await WaitUntilAsync(() => db.VisitCount >= 2);
        await cts.CancelAsync();

        Assert.NotEqual(db.InsertedVisits[0].HashedIp, db.InsertedVisits[1].HashedIp);
    }

    [Fact]
    public async Task Writer_DerivesDimensions_AndDoesNotPersistRawSignals()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        var evt = new VisitEvent(
            ShortCodeId: Guid.CreateVersion7(),
            UserLinkCodeId: null,
            UserId: null,
            SocialPostCodeId: null,
            RawIp: "1.2.3.4",
            Referrer: "https://www.t.co/abc123?s=secret",
            UserAgent: "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148 Safari/604.1",
            ClickedAt: DateTimeOffset.UtcNow,
            AcceptLanguage: "en-US,en;q=0.9",
            SecFetchSite: "cross-site",
            RawQuery: "?utm_source=newsletter&utm_medium=email&utm_campaign=launch&other=dropped");

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(evt);
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        var stored = db.InsertedVisits.Single();
        Assert.Equal(ShortLynx.Data.Enums.ClickSource.Twitter, stored.Source);
        Assert.Equal(ShortLynx.Data.Enums.DeviceType.Mobile, stored.Device);
        Assert.Equal("Safari", stored.Browser);
        Assert.Equal("iOS", stored.Os);
        Assert.Equal("t.co", stored.ReferrerHost);      // host only, www stripped, path/query dropped
        Assert.Equal("en", stored.Language);
        Assert.Equal("cross-site", stored.NavigationType);
        Assert.Equal("US", stored.Country);
        Assert.Equal("America/Chicago", stored.TimeZone);
        Assert.Equal("newsletter", stored.UtmSource);
        Assert.Equal("email", stored.UtmMedium);
        Assert.Equal("launch", stored.UtmCampaign);
        Assert.Null(stored.UtmTerm); // absent tag stays null; non-UTM params are never stored
    }

    [Fact]
    public async Task Writer_PrivacySignal_CountsClick_ButSuppressesDimensions()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        var evt = new VisitEvent(
            ShortCodeId: Guid.CreateVersion7(),
            UserLinkCodeId: null,
            UserId: null,
            SocialPostCodeId: null,
            RawIp: "1.2.3.4",
            Referrer: "https://t.co/abc",
            UserAgent: "Mozilla/5.0 (iPhone) Mobile Safari",
            ClickedAt: DateTimeOffset.UtcNow,
            AcceptLanguage: "en-US",
            SecFetchSite: "cross-site",
            PrivacySignal: true,
            RawQuery: "?utm_source=newsletter");

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(evt);
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        var stored = db.InsertedVisits.Single();
        Assert.Equal(64, stored.HashedIp.Length);       // click still counted
        Assert.Null(stored.Browser);
        Assert.Null(stored.Os);
        Assert.Null(stored.ReferrerHost);
        Assert.Null(stored.Language);
        Assert.Null(stored.NavigationType);
        Assert.Null(stored.Country);   // geo suppressed under a privacy signal too
        Assert.Null(stored.TimeZone);
        Assert.Null(stored.UtmSource); // UTM suppressed under a privacy signal like every dimension
        Assert.Equal(ShortLynx.Data.Enums.DeviceType.Unknown, stored.Device);
    }

    [Fact]
    public async Task Writer_RespectsConfiguredBatchSize()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20, batchSize: 2);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        for (var i = 0; i < 5; i++) await sink.EnqueueAsync(Mode1Event());
        await WaitUntilAsync(() => db.VisitCount >= 5);
        await cts.CancelAsync();

        Assert.Equal(5, db.InsertedVisits.Count);
    }

    [Fact]
    public async Task Writer_CityEligibleAccount_UpsertsCityClicks()
    {
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        // Seed one account with EnableCityAggregates on, one link, one short code -- the minimum
        // graph ResolveCityEligibilityAsync needs to say yes.
        Guid shortCodeId;
        await using (var seed = testDb.CreateContext())
        {
            var accountId = Guid.CreateVersion7();
            var linkId = Guid.CreateVersion7();
            shortCodeId = Guid.CreateVersion7();
            seed.AccountEntities.Add(new ShortLynx.Data.Entities.AccountEntity
            {
                Id = accountId, Name = "Acme", CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
                PrivacyPolicyUrl = "https://acme.example/privacy", EnableCityAggregates = true,
            });
            seed.LinkEntities.Add(new ShortLynx.Data.Entities.LinkEntity
            {
                Id = linkId, AccountId = accountId, OriginalUrl = "https://acme.example",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
                Mode = ShortLynx.Data.Enums.LinkMode.Anonymous,
            });
            seed.ShortCodeEntities.Add(new ShortLynx.Data.Entities.ShortCodeEntity
            {
                Id = shortCodeId, LinkId = linkId, Code = "abc123",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            });
            await seed.SaveChangesAsync();
        }

        var evt = new VisitEvent(
            ShortCodeId: shortCodeId, UserLinkCodeId: null, UserId: null, SocialPostCodeId: null,
            RawIp: "1.2.3.4", Referrer: null, UserAgent: "test-agent", ClickedAt: DateTimeOffset.UtcNow);

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(evt);
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        var item = Assert.Single(db.UpsertedCityClicks);
        Assert.Equal("Chicago", item.City); // StubGeoIpResolver's city answer when includeCity is true
        Assert.Equal("US", item.Country);
    }

    [Fact]
    public async Task Writer_IneligibleAccount_NeverUpsertsCityClicks()
    {
        // The default for every account -- EnableCityAggregates unset -- must produce zero city rows,
        // even though the stub resolver would happily hand back a city if asked.
        var (sink, db, writer, testDb) = await MakeWriter(drainMs: 20);
        await using var _ = testDb;
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event()); // ShortCodeId points at nothing seeded in the test DB
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        Assert.Empty(db.UpsertedCityClicks);
    }

    [Fact]
    public void HashIp_SameIpAndPepper_IsConsistent()
    {
        var h1 = BackgroundVisitWriter.HashIp("10.0.0.1", "pepper");
        var h2 = BackgroundVisitWriter.HashIp("10.0.0.1", "pepper");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void HashIp_DifferentInputs_DifferentHashes()
    {
        var h1 = BackgroundVisitWriter.HashIp("1.1.1.1", "pepper");
        var h2 = BackgroundVisitWriter.HashIp("2.2.2.2", "pepper");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashIp_DifferentPepper_ProducesDifferentHash()
    {
        // The secret pepper is what makes the hash non-reversible: the same IP under two different
        // peppers must not be linkable.
        var h1 = BackgroundVisitWriter.HashIp("203.0.113.7", "pepper-A");
        var h2 = BackgroundVisitWriter.HashIp("203.0.113.7", "pepper-B");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashIp_SameIpSameRotationDay_DifferentUtcHours_ProducesSameHash()
    {
        // 6am and 11pm Eastern on the same calendar day are hours apart in UTC terms, but both fall in
        // the same 5am-anchored rotation day -- this is the whole point of moving off hourly rotation.
        // January is unambiguously EST (UTC-5) everywhere in the US -- no DST-transition-date math needed.
        var morning = new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero); // 6am EST
        var night = new DateTimeOffset(2026, 1, 16, 3, 0, 0, TimeSpan.Zero);    // 10pm EST same rotation day

        var h1 = BackgroundVisitWriter.HashIp("203.0.113.9", "pepper", morning);
        var h2 = BackgroundVisitWriter.HashIp("203.0.113.9", "pepper", night);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void HashIp_AcrossThe5amEasternBoundary_ProducesDifferentHash()
    {
        var before5am = new DateTimeOffset(2026, 1, 15, 9, 59, 0, TimeSpan.Zero); // 4:59am EST
        var after5am = new DateTimeOffset(2026, 1, 15, 10, 1, 0, TimeSpan.Zero);  // 5:01am EST

        var h1 = BackgroundVisitWriter.HashIp("203.0.113.9", "pepper", before5am);
        var h2 = BackgroundVisitWriter.HashIp("203.0.113.9", "pepper", after5am);
        Assert.NotEqual(h1, h2);
    }

    [Theory]
    [InlineData(2026, 1, 15, 9, 59, "20260114")]  // just before 5am EST (winter, UTC-5) -> previous day's bucket
    [InlineData(2026, 1, 15, 10, 1, "20260115")]  // just after 5am EST
    [InlineData(2026, 7, 15, 8, 59, "20260714")]  // just before 5am EDT (summer, UTC-4) -> previous day's bucket
    [InlineData(2026, 7, 15, 9, 1, "20260715")]   // just after 5am EDT
    public void DailyBucket_HandlesBothDstOffsets(int y, int m, int d, int h, int min, string expected)
    {
        var utc = new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero);
        Assert.Equal(expected, BackgroundVisitWriter.DailyBucket(utc));
    }

    // Fixed geo answer so tests can assert the writer stores exactly country + timezone and no more --
    // and, when includeCity is true, a fixed city so the city-aggregation path is exercisable too.
    private sealed class StubGeoIpResolver : ShortLynx.Services.Analytics.IGeoIpResolver
    {
        public ShortLynx.Services.Analytics.GeoLocation Resolve(string rawIp, bool includeCity = false)
            => new("US", "America/Chicago", includeCity ? "Chicago" : null);
    }
}
