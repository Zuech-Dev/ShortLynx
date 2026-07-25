namespace ShortLynx.Core.Options;

/// <summary>Bound from the "LiveStream" config section. Tuning for the SSE click feed (<c>GET /me/stream</c>).</summary>
public sealed class LiveStreamOptions
{
    /// <summary>How often the stream re-queries for new clicks. The visit writer flushes every 500ms
    /// by default, so anything below ~1s costs queries without reducing observed latency.</summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>
    /// How far *behind* the high-water mark each poll re-queries. Covers the gap between a click's
    /// <c>ClickedAt</c> and the batch write that makes it visible; rows already sent are suppressed by
    /// the de-duplication set. Too small silently drops clicks under write lag, so this is deliberately
    /// several times the writer's drain interval.
    /// </summary>
    public int OverlapSeconds { get; set; } = 30;

    /// <summary>Max clicks returned per poll. A burst above this drains over subsequent polls rather
    /// than arriving as one oversized frame.</summary>
    public int MaxEventsPerPoll { get; set; } = 200;

    /// <summary>Seconds between heartbeat comments. Proxies and load balancers close idle connections;
    /// a low-traffic account can legitimately produce no events for hours.</summary>
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>
    /// Hard cap on a single connection's lifetime. EventSource reconnects on its own, so recycling
    /// bounds the leak from connections that are never cleanly closed and gives the server a chance to
    /// re-read the account's links from scratch.
    /// </summary>
    public int MaxConnectionMinutes { get; set; } = 30;

    // The per-IP concurrency cap lives in RateLimitOptions.StreamConcurrencyLimit, with the other
    // limiter settings, rather than being split across two config sections.
}
