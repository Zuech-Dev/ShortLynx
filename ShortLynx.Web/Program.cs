using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using ShortLynx.Repository;
using ShortLynx.Services.Visits;
using ShortLynx.Services.Redirect;
using ShortLynx.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Honour X-Forwarded-* from Railway's edge proxy so the client IP (rate limiting, analytics IP hashing)
// and original scheme (HTTPS redirect) are correct. Railway's edge IP is dynamic, so we can't pin a
// KnownProxy; instead we trust one upstream hop unconditionally — sound because the container is only
// reachable through that edge (no direct ingress), and Railway's Envoy writes the rightmost
// X-Forwarded-For entry. WITHOUT a trusted network the middleware silently drops X-Forwarded-*, leaving
// RemoteIpAddress as an internal Railway address that varies per connection — which is why per-IP rate
// limiting didn't partition real clients together in production.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1; // trust exactly one hop (the edge); the rightmost XFF entry is Railway's
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

// Short-link redirect endpoint — must come after Razor Pages so literal routes (/Privacy, /Error)
// take precedence over the /{code} parameter route.
app.MapGet("/{code}", async (
    string code,
    HttpContext ctx,
    IRedirectService redirectSvc,
    IVisitEventSink sink) =>
{
    var entry = await redirectSvc.LookupAsync(code, ctx.Request.Host.Host, ctx.RequestAborted);
    if (entry is null) return Results.NotFound();

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
}).RequireRateLimiting("redirect");

app.Run();
