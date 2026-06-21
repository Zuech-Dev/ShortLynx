using Microsoft.AspNetCore.HttpOverrides;
using ShortLynx.Services.Visits;
using ShortLynx.Services.Redirect;
using ShortLynx.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Honour X-Forwarded-* from the hosting proxy (Railway) — without this, every redirect's client IP
// collapses to the proxy IP (breaking per-IP rate limiting and visit analytics).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRazorPages();
builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxRedirect(builder.Configuration);
builder.Services.AddShortLynxRateLimiter(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

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

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var referrer = ctx.Request.Headers.Referer.ToString();
    var ua = ctx.Request.Headers.UserAgent.ToString();

    await sink.EnqueueAsync(new VisitEvent(
        ShortCodeId: entry.ShortCodeId,
        UserLinkCodeId: entry.UserLinkCodeId,
        UserId: entry.UserId,
        RawIp: ip,
        Referrer: referrer.Length > 0 ? referrer : null,
        UserAgent: ua.Length > 0 ? ua : null,
        ClickedAt: DateTimeOffset.UtcNow));

    return Results.Redirect(entry.OriginalUrl, permanent: false);
}).RequireRateLimiting("redirect");

app.Run();
