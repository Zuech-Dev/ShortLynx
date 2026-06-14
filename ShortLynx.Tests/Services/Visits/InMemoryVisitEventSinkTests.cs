using Microsoft.Extensions.Options;
using ShortLynx.Services.Visits;

namespace ShortLynx.Tests.Services.Visits;

public class InMemoryVisitEventSinkTests
{
    private static InMemoryVisitEventSink MakeSink(int capacity = 10)
        => new(Options.Create(new VisitSinkOptions { ChannelCapacity = capacity }));

    private static VisitEvent AnonymousEvent() => new(
        ShortCodeId: Guid.CreateVersion7(),
        UserLinkCodeId: null,
        UserId: null,
        RawIp: "1.2.3.4",
        Referrer: null,
        UserAgent: null,
        ClickedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task EnqueueAsync_DoesNotThrow()
    {
        var sink = MakeSink();
        await sink.EnqueueAsync(AnonymousEvent()); // must not throw
    }

    [Fact]
    public async Task EnqueueAsync_ItemAppearsOnReader()
    {
        var sink = MakeSink();
        var evt = AnonymousEvent();
        await sink.EnqueueAsync(evt);

        Assert.True(sink.Reader.TryRead(out var read));
        Assert.Equal(evt.ShortCodeId, read!.ShortCodeId);
    }

    [Fact]
    public async Task EnqueueAsync_MultipleItems_AllReadable()
    {
        var sink = MakeSink(capacity: 20);
        for (var i = 0; i < 5; i++)
            await sink.EnqueueAsync(AnonymousEvent());

        var count = 0;
        while (sink.Reader.TryRead(out _)) count++;
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task EnqueueAsync_WhenFull_DropsOldestNotThrows()
    {
        // Capacity = 2; enqueue 3 items — oldest gets dropped silently.
        var sink = MakeSink(capacity: 2);
        for (var i = 0; i < 3; i++)
            await sink.EnqueueAsync(AnonymousEvent());

        // Must not throw; exactly 2 items remain (oldest was dropped).
        var count = 0;
        while (sink.Reader.TryRead(out _)) count++;
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsCompletedValueTask()
    {
        var sink = MakeSink();
        var vt = sink.EnqueueAsync(AnonymousEvent());
        Assert.True(vt.IsCompleted);
        await vt; // must not throw
    }
}
