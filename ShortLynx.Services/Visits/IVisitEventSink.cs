namespace ShortLynx.Services.Visits;

public interface IVisitEventSink
{
    ValueTask EnqueueAsync(VisitEvent evt, CancellationToken ct = default);
}
