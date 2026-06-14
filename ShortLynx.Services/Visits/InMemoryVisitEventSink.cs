using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.Visits;

public sealed class InMemoryVisitEventSink : IVisitEventSink
{
    private readonly Channel<VisitEvent> _channel;

    public InMemoryVisitEventSink(IOptions<VisitSinkOptions> options)
    {
        _channel = Channel.CreateBounded<VisitEvent>(
            new BoundedChannelOptions(options.Value.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
    }

    public ValueTask EnqueueAsync(VisitEvent evt, CancellationToken ct = default)
    {
        _channel.Writer.TryWrite(evt);
        return ValueTask.CompletedTask;
    }

    // Exposed for BackgroundVisitWriter, which lives in the same assembly.
    public ChannelReader<VisitEvent> Reader => _channel.Reader;
}
