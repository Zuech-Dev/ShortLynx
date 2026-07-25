using Microsoft.AspNetCore.Mvc;
using ShortLynx.Data.Context;
using ShortLynx.Services.Visits;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Raw, account-wide clicks for a time window — the historical half of the live feed.
///
/// The existing analytics endpoints are per-link or per-campaign and pre-aggregated, which suits a
/// detail page but can't drive an account-level dashboard: rolling up N links means N requests, and
/// pre-aggregated counts can't be re-filtered without another round trip per filter change. This
/// returns the same row shape <c>GET /me/stream</c> pushes, so a client loads a window once, appends
/// live clicks to it, and does its aggregation and include/exclude filtering over one array —
/// historical and live data behaving identically because they *are* the same shape.
///
/// The trade-off is an explicit row ceiling rather than unbounded aggregation. An account past
/// <see cref="MaxLimit"/> clicks in the requested window gets the oldest rows in it and should narrow
/// the window; a server-side aggregate endpoint is the answer if that becomes the common case.
/// </summary>
[Route("me/clicks")]
public class MeClicksController(ShortLynxDbContext db) : SessionControllerBase
{
    /// <summary>Hard cap on rows returned, whatever the caller asks for.</summary>
    public const int MaxLimit = 5000;

    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Clicks on this account's links, oldest first. <paramref name="since"/> defaults to 30 days ago;
    /// <paramref name="limit"/> is clamped to <see cref="MaxLimit"/>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? since,
        [FromQuery] int limit = 1000,
        CancellationToken ct = default)
    {
        var from = since ?? DateTimeOffset.UtcNow.AddDays(-DefaultWindowDays);
        var capped = Math.Clamp(limit, 1, MaxLimit);

        var rows = await LiveVisitQueries.LoadSinceAsync(db, AccountId, from, capped, ct);

        return Ok(new
        {
            since = from,
            // Lets a client tell "this is everything" from "this is the first page of a larger window"
            // without comparing counts against a limit it would have to know.
            truncated = rows.Count >= capped,
            clicks = rows.Select(r => new
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
            }),
        });
    }
}
