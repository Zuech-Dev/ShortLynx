using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Options;
using ShortLynx.Core.RateLimit;
using ShortLynx.Data.Context;
using ShortLynx.Services.Visits;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// The live click feed. A dashboard opens one <c>EventSource</c> against <c>GET /me/stream</c> and
/// receives every click on the account's links as it lands.
///
/// SSE rather than WebSockets on purpose: the feed is strictly server→client, EventSource reconnects
/// by itself, and it rides plain HTTP — so it needs no new infrastructure and no sticky sessions at the
/// edge. Authentication is the ordinary session cookie, which matters because EventSource cannot set an
/// <c>Authorization</c> header; the JWT bearer scheme is already configured to fall back to the access
/// cookie, so no auth change was needed here.
/// </summary>
[Route("me/stream")]
[EnableRateLimiting(RateLimitPolicies.Stream)]
public class MeStreamController(
    ShortLynxDbContext db,
    IOptions<LiveStreamOptions> options,
    ILogger<MeStreamController> logger) : SessionControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Streams clicks as they land. Pass <c>?since=</c> (ISO-8601) to backfill from a known point —
    /// that is how a reconnecting client closes the gap it was disconnected for. Omitted, the stream
    /// starts from now and only reports genuinely new clicks.
    /// </summary>
    [HttpGet]
    public async Task Stream([FromQuery] DateTimeOffset? since, CancellationToken ct)
    {
        var o = options.Value;

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        // nginx and several CDN edges buffer proxied responses by default, which holds every event
        // until the buffer fills — the stream then arrives in clumps, or not at all. This opts out.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Bound the connection's life. EventSource reconnects transparently, so recycling costs the
        // client nothing and stops a half-open connection from polling forever.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(o.MaxConnectionMinutes));
        var token = lifetime.Token;

        // The high-water mark. Each poll queries from (cursor - overlap) because a click's ClickedAt is
        // stamped at redirect time but written a batch later, so rows do not become visible in
        // ClickedAt order — see LiveVisitQueries.
        var cursor = since ?? DateTimeOffset.UtcNow;
        var overlap = TimeSpan.FromSeconds(o.OverlapSeconds);

        // Ids already delivered, so the overlap window doesn't re-send them. Pruned to the window on
        // every poll — without that this grows for the life of the connection.
        var sent = new Dictionary<Guid, DateTimeOffset>();

        await WriteAsync($"event: ready\ndata: {JsonSerializer.Serialize(new { serverTime = DateTimeOffset.UtcNow, since = cursor }, Json)}\n\n", token);

        var lastHeartbeat = DateTimeOffset.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var windowStart = cursor - overlap;

                List<LiveVisitRow> rows;
                try
                {
                    rows = await LiveVisitQueries.LoadSinceAsync(db, AccountId, windowStart, o.MaxEventsPerPoll, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // A transient database blip must not kill the stream — the client would reconnect
                    // into the same failure and lose its cursor. Skip this poll and try the next.
                    logger.LogWarning(ex, "Live stream poll failed for account {AccountId}; retrying next interval", AccountId);
                    rows = [];
                }

                foreach (var row in rows)
                {
                    if (!sent.TryAdd(row.Id, row.ClickedAt)) continue;

                    await WriteAsync($"id: {row.Id}\nevent: click\ndata: {JsonSerializer.Serialize(ToPayload(row), Json)}\n\n", token);

                    if (row.ClickedAt > cursor) cursor = row.ClickedAt;
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                // Anything older than the window can never come back in a future query, so remembering
                // it serves no purpose.
                var prunePoint = cursor - overlap;
                if (sent.Count > 0)
                {
                    foreach (var id in sent.Where(kv => kv.Value < prunePoint).Select(kv => kv.Key).ToList())
                        sent.Remove(id);
                }

                // A comment frame: ignored by EventSource, but it keeps the connection observably alive
                // for intermediaries that would otherwise time it out.
                if (DateTimeOffset.UtcNow - lastHeartbeat >= TimeSpan.FromSeconds(o.HeartbeatSeconds))
                {
                    await WriteAsync($": ping {DateTimeOffset.UtcNow:O}\n\n", token);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                await Task.Delay(o.PollIntervalMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // The client navigated away, or the connection hit its lifetime cap. Both are the normal
            // way this method ends, not an error.
        }
    }

    private static object ToPayload(LiveVisitRow r) => new
    {
        id = r.Id,
        linkId = r.LinkId,
        code = r.Code,
        userId = r.UserId,
        clickedAt = r.ClickedAt,
        source = r.Source.ToString(),
        device = r.Device.ToString(),
        browser = r.Browser,
        os = r.Os,
        country = r.Country,
        timeZone = r.TimeZone,
        language = r.Language,
        referrerHost = r.ReferrerHost,
        utmSource = r.UtmSource,
        utmMedium = r.UtmMedium,
        utmCampaign = r.UtmCampaign,
    };

    private async Task WriteAsync(string frame, CancellationToken ct)
    {
        await Response.WriteAsync(frame, ct);
        // Explicit flush per frame: without it the response buffer holds events until it fills, which
        // is exactly the latency this endpoint exists to avoid.
        await Response.Body.FlushAsync(ct);
    }
}
