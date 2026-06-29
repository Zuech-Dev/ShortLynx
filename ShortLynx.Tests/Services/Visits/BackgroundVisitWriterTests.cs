using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Operations;
using ShortLynx.Services.Visits;

namespace ShortLynx.Tests.Services.Visits;

public class BackgroundVisitWriterTests
{
    private static (InMemoryVisitEventSink Sink, FakeDbOperations Db, BackgroundVisitWriter Writer)
        MakeWriter(int drainMs = 20, int batchSize = 100)
    {
        var opts = Options.Create(new VisitSinkOptions
        {
            ChannelCapacity = 1_000,
            BatchSize = batchSize,
            DrainIntervalMs = drainMs,
        });
        var db = new FakeDbOperations();

        // BackgroundVisitWriter uses IServiceScopeFactory to resolve IDbOperations per flush.
        var services = new ServiceCollection();
        services.AddSingleton<IDbOperations>(db);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var sink = new InMemoryVisitEventSink(opts);
        var writer = new BackgroundVisitWriter(sink, scopeFactory, opts);
        return (sink, db, writer);
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
            RawIp: ip,
            Referrer: null,
            UserAgent: null,
            ClickedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Writer_RoutesMode1Events_To_BulkInsertVisits()
    {
        var (sink, db, writer) = MakeWriter(drainMs: 20);
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
        var (sink, db, writer) = MakeWriter(drainMs: 20);
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
        var (sink, db, writer) = MakeWriter(drainMs: 20);
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
        var (sink, db, writer) = MakeWriter(drainMs: 20);
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
    public async Task Writer_SameIpSameHour_ProducesSameHash()
    {
        const string rawIp = "203.0.113.1";
        var (sink, db, writer) = MakeWriter(drainMs: 20);
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
        var (sink, db, writer) = MakeWriter(drainMs: 20);
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(Mode1Event("1.1.1.1"));
        await sink.EnqueueAsync(Mode1Event("2.2.2.2"));
        await WaitUntilAsync(() => db.VisitCount >= 2);
        await cts.CancelAsync();

        Assert.NotEqual(db.InsertedVisits[0].HashedIp, db.InsertedVisits[1].HashedIp);
    }

    [Fact]
    public async Task Writer_PreservesReferrerAndUserAgent()
    {
        var (sink, db, writer) = MakeWriter(drainMs: 20);
        using var cts = new CancellationTokenSource();

        var evt = new VisitEvent(
            ShortCodeId: Guid.CreateVersion7(),
            UserLinkCodeId: null,
            UserId: null,
            RawIp: "1.2.3.4",
            Referrer: "https://example.com",
            UserAgent: "Mozilla/5.0",
            ClickedAt: DateTimeOffset.UtcNow);

        await writer.StartAsync(cts.Token);
        await sink.EnqueueAsync(evt);
        await WaitUntilAsync(() => db.VisitCount >= 1);
        await cts.CancelAsync();

        var stored = db.InsertedVisits.Single();
        Assert.Equal("https://example.com", stored.Referrer);
        Assert.Equal("Mozilla/5.0", stored.UserAgent);
    }

    [Fact]
    public async Task Writer_RespectsConfiguredBatchSize()
    {
        var (sink, db, writer) = MakeWriter(drainMs: 20, batchSize: 2);
        using var cts = new CancellationTokenSource();

        await writer.StartAsync(cts.Token);
        for (var i = 0; i < 5; i++) await sink.EnqueueAsync(Mode1Event());
        await WaitUntilAsync(() => db.VisitCount >= 5);
        await cts.CancelAsync();

        Assert.Equal(5, db.InsertedVisits.Count);
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
}
