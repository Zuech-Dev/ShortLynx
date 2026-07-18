using Microsoft.AspNetCore.HttpOverrides;
using ShortLynx.Repository;
using ShortLynx.Services.Visits;
using ShortLynx.Services.Redirect;
using ShortLynx.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Honour X-Forwarded-* from the hosting proxy (Railway) — without this, every redirect's client IP
// collapses to the proxy IP (breaking per-IP rate limiting and visit analytics).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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
