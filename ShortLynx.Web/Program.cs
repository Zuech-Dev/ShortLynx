using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ShortLynx.Repository;
using ShortLynx.Services.Observability;
using ShortLynx.Services.Visits;
using ShortLynx.Services.Redirect;
using ShortLynx.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddShortLynxLogging();

// Error tracking, opt-in — unset means exactly today's behavior, no outbound telemetry at all. See
// ObservabilityExtensions.cs's doc comment for why UseSentry() is called here directly rather than
// from that shared method (it needs the ASP.NET Core shared framework, which only a Web SDK app has).
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        o.Environment = builder.Environment.EnvironmentName;
        // Explicit, even though it's the SDK default: no request bodies, full headers, or user claims
        // leave the process. Belt-and-suspenders on top of ScrubSensitiveData, matching how VisitSink
        // treats IP hashing (pepper AND hourly rotation, not just one).
        o.SendDefaultPii = false;
        o.SetBeforeSend((@event, _) => ObservabilityExtensions.ScrubSensitiveData(@event));
    });
}

// Honour X-Forwarded-* from Railway's edge proxy so the client IP (rate limiting, analytics IP hashing)
// and original scheme (HTTPS redirect) are correct. Railway's edge IP is dynamic, so we can't pin a
// KnownProxy; instead the upstream hops are trusted unconditionally — sound because the container is
// only reachable through that edge (no direct ingress). WITHOUT a trusted network the middleware
// silently drops X-Forwarded-* altogether.
//
// ForwardLimit must equal the number of hops the edge actually appends. Measured against the live
// deployment 2026-07-25, the header arrives with TWO entries:
//     X-Forwarded-For: <real client>, <Railway edge>
// The middleware consumes entries right-to-left, so ForwardLimit=1 consumes only Railway's own entry
// and leaves RemoteIpAddress set to *that* — an internal address that rotates between connections.
// Consequences, both confirmed in production: every per-IP rate limiter silently stopped working (no
// burst ever shared a partition, so nothing was ever throttled), and in ShortLynx.Web the same value
// feeds the visit record's RawIp -> HashedIp and GeoIP country lookup, so click analytics were
// attributed to Railway's infrastructure instead of the visitor. ForwardLimit=2 steps past the edge
// hop onto the real client.
//
// Overridable by configuration so a change in edge topology — or an added layer, e.g. Cloudflare in
// front of a custom domain — can be corrected without shipping code. It is deliberately an exact hop
// count rather than an "unlimited" mode: unlimited would walk to the leftmost entry, which a client
// can forge, making the resolved IP attacker-controlled.
// Bound through IConfiguration lazily (not read eagerly off builder.Configuration) so the value is
// resolved after the host is fully composed — that keeps the knob genuinely overridable, including by
// test hosts that add their configuration during Build rather than before it.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, cfg) =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = cfg.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.IPv6Any, 0)); // ::/0 — Railway's internal mesh is IPv6
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Any, 0));     // 0.0.0.0/0 — belt and suspenders
});

builder.Services.AddRazorPages();
builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxRedirect(builder.Configuration);
builder.Services.AddShortLynxRateLimiter(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Dev-only guard: fail fast at startup if the database is behind the migrations, so schema drift
// (a generated-but-unapplied migration) surfaces here instead of as a cryptic query-time error like
// "column does not exist". Resolve with: dotnet ef database update.
if (app.Environment.IsDevelopment())
    DatabaseMigrationGuard.ThrowIfPending(app.Services);

app.UseForwardedHeaders();

// Security headers on every response. The landing/marketing pages have no forms and no user input,
// so the main hardening is defence-in-depth: block framing/sniffing, tight referrer, and a CSP that
// only permits first-party assets (the redirect endpoint returns a bodyless 302, so CSP is a no-op
// there). Volumetric DDoS is handled at the edge (see DEPLOY.md) — the landing page is static and
// touches no database, so it's cheap to serve under load.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapHealthChecks("/health");

// Custom (vanity) codes under the configured prefix (default /c/), isolated from the root namespace.
// Two segments, so it never collides with the single-segment literal routes or /{code}. Anonymous
// only: no disclosure gate, no one-time handling.
var customPrefix = (builder.Configuration.GetSection("ShortCode")["CustomRoutePrefix"] ?? "c").Trim('/');
app.MapGet($"/{customPrefix}/{{code}}", async (
    string code,
    HttpContext ctx,
    IRedirectService redirectSvc,
    IVisitEventSink sink,
    IOptions<RedirectOptions> redirectOptions) =>
{
    var entry = await redirectSvc.LookupCustomAsync(code, ctx.Request.Host.Host, ctx.RequestAborted);
    return entry is null
        ? NotFoundResult(redirectOptions.Value)
        : await RecordVisitAndRedirect(ctx, entry, sink, anonByChoice: false);
}).RequireRateLimiting("redirect");

// Short-link redirect endpoint — must come after Razor Pages so literal routes (/Privacy, /Error)
// take precedence over the /{code} parameter route.
app.MapGet("/{code}", async (
    string code,
    HttpContext ctx,
    IRedirectService redirectSvc,
    IVisitEventSink sink,
    IOptions<RedirectOptions> redirectOptions) =>
{
    var entry = await redirectSvc.LookupAsync(code, ctx.Request.Host.Host, ctx.RequestAborted);
    if (entry is null) return NotFoundResult(redirectOptions.Value);

    // Mode 2 disclosure gate: when the operator has no privacy policy, the recipient must have made
    // a choice (30-day preference cookie) before any tracking fires; otherwise pause on the
    // interstitial. "anon" is honoured exactly like a DNT header.
    var anonByChoice = false;
    if (entry is { DisclosureRequired: true, UserLinkCodeId: not null })
    {
        var pref = ctx.Request.Cookies[$"sl_pref_{entry.AccountId}"];
        if (pref is not ("allow" or "anon"))
            return Results.Redirect($"/disclosure/{Uri.EscapeDataString(code)}", permanent: false);
        anonByChoice = pref == "anon";
    }

    // One-time codes are claimed here — after the disclosure choice — so rendering the interstitial
    // can't burn them. Losing the race (or a replay) behaves like an unknown code.
    if (entry is { IsOneTimeUse: true, UserLinkCodeId: not null } &&
        !await redirectSvc.TryClaimOneTimeAsync(entry.UserLinkCodeId.Value, ctx.RequestAborted))
        return Results.NotFound();

    return await RecordVisitAndRedirect(ctx, entry, sink, anonByChoice);
}).RequireRateLimiting("redirect");

app.Run();

// Shared miss path for both routes above. Empty/unset NotFoundRedirectUrl (the default for every
// deployment that hasn't explicitly opted in) keeps today's behavior exactly: a plain 404.
static IResult NotFoundResult(RedirectOptions options)
    => string.IsNullOrWhiteSpace(options.NotFoundRedirectUrl)
        ? Results.NotFound()
        : Results.Redirect(options.NotFoundRedirectUrl, permanent: false);

// Shared tail of both redirect paths: derive the request signals, enqueue the async visit event, 302.
static async Task<IResult> RecordVisitAndRedirect(HttpContext ctx, RedirectCacheEntry entry, IVisitEventSink sink, bool anonByChoice)
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var referrer = ctx.Request.Headers.Referer.ToString();
    var ua = ctx.Request.Headers.UserAgent.ToString();
    var acceptLanguage = ctx.Request.Headers.AcceptLanguage.ToString();
    var secFetchSite = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
    // Honour an explicit "do not track" preference (DNT:1 or the newer Sec-GPC:1).
    var privacySignal = ctx.Request.Headers["DNT"] == "1" || ctx.Request.Headers["Sec-GPC"] == "1";

    await sink.EnqueueAsync(new VisitEvent(
        ShortCodeId: entry.ShortCodeId,
        UserLinkCodeId: entry.UserLinkCodeId,
        UserId: entry.UserId,
        SocialPostCodeId: entry.SocialPostCodeId,
        RawIp: ip,
        Referrer: referrer.Length > 0 ? referrer : null,
        UserAgent: ua.Length > 0 ? ua : null,
        ClickedAt: DateTimeOffset.UtcNow,
        AcceptLanguage: acceptLanguage.Length > 0 ? acceptLanguage : null,
        SecFetchSite: secFetchSite.Length > 0 ? secFetchSite : null,
        PrivacySignal: privacySignal || anonByChoice,
        RawQuery: ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null));

    return Results.Redirect(entry.OriginalUrl, permanent: false);
}
