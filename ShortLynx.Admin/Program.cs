using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Admin.Components;
using ShortLynx.Admin.Extensions;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Repository;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Links;
using ShortLynx.Services.Qr;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.Social;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();

builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxServices(builder.Configuration);
builder.Services.AddShortLynxAuth();
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
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages();
app.MapHealthChecks("/health");

// QR code download for a link in the signed-in user's account. PNG (default) or SVG (?format=svg);
// ?code= picks a recipient code for user-attributed links. A plain authenticated GET — linked from
// the link-detail page with a `download` attribute.
app.MapGet("/qr/{linkId:guid}", async (
        Guid linkId, string? format, int? size, string? code,
        ClaimsPrincipal user,
        IDbContextFactory<ShortLynxDbContext> dbFactory,
        IQrCodeService qr,
        IOptions<DashboardOptions> dashboard,
        IOptions<ShortCodeOptions> shortCodeOptions,
        CancellationToken ct) =>
    {
        var userId = user.GetUserId();
        if (userId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accountId = await AccountResolver.ResolveAccountIdAsync(
            db, userId.Value, user.GetAccountId(), user.Identity?.Name ?? "Personal", ct);

        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (link is null) return Results.NotFound();

        string? targetCode = code;
        bool isCustom;
        if (targetCode is null)
        {
            if (link.Mode != LinkMode.Anonymous) return Results.BadRequest("A code is required for user-attributed links.");
            var sc = await db.ShortCodeEntities.Where(x => x.LinkId == linkId)
                .Select(x => new { x.Code, x.IsCustom }).FirstOrDefaultAsync(ct);
            targetCode = sc?.Code;
            isCustom = sc?.IsCustom ?? false;
        }
        else if (link.Mode == LinkMode.Anonymous)
        {
            var sc = await db.ShortCodeEntities.Where(x => x.LinkId == linkId && x.Code == targetCode)
                .Select(x => new { x.IsCustom }).FirstOrDefaultAsync(ct);
            if (sc is null) return Results.NotFound();
            isCustom = sc.IsCustom;
        }
        else
        {
            var belongs = await db.UserLinkCodeEntities.AnyAsync(c => c.LinkId == linkId && c.Code == targetCode, ct);
            if (!belongs) return Results.NotFound();
            isCustom = false;
        }
        if (string.IsNullOrEmpty(targetCode)) return Results.NotFound();

        var url = await ShortUrlBuilder.BuildAsync(
            db, link, targetCode, isCustom, shortCodeOptions.Value.CustomRoutePrefix, dashboard.Value.PublicBaseUrl, ct);
        return (format ?? "png").ToLowerInvariant() switch
        {
            "svg" => Results.File(System.Text.Encoding.UTF8.GetBytes(qr.GenerateSvg(url, size ?? 10)), "image/svg+xml", $"{targetCode}.svg"),
            "png" => Results.File(qr.GeneratePng(url, size ?? 10), "image/png", $"{targetCode}.png"),
            var f => Results.BadRequest($"Unknown format '{f}'. Use 'png' or 'svg'."),
        };
    })
    .RequireAuthorization();

// Aggregate-only analytics CSV downloads for the dashboard (MASTER_PLAN P2: never row-per-click).
// Same authenticated-GET pattern as /qr; linked from the link/campaign detail pages.
app.MapGet("/export/link/{linkId:guid}", async (
        Guid linkId, ClaimsPrincipal user,
        IDbContextFactory<ShortLynxDbContext> dbFactory, CancellationToken ct) =>
    {
        var userId = user.GetUserId();
        if (userId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accountId = await AccountResolver.ResolveAccountIdAsync(
            db, userId.Value, user.GetAccountId(), user.Identity?.Name ?? "Personal", ct);

        var link = await db.LinkEntities.FirstOrDefaultAsync(l => l.Id == linkId && l.AccountId == accountId, ct);
        if (link is null) return Results.NotFound();

        var rows = await ShortLynx.Services.Analytics.LinkVisitQueries.LoadLinkRowsAsync(db, link, ct);
        var csv = ShortLynx.Services.Analytics.ClickBreakdownCsv.Format(
            ShortLynx.Services.Analytics.ClickAggregator.Summarize(rows));
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"link-{linkId}-analytics.csv");
    })
    .RequireAuthorization();

app.MapGet("/export/campaign/{campaignId:guid}", async (
        Guid campaignId, ClaimsPrincipal user,
        IDbContextFactory<ShortLynxDbContext> dbFactory, CancellationToken ct) =>
    {
        var userId = user.GetUserId();
        if (userId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accountId = await AccountResolver.ResolveAccountIdAsync(
            db, userId.Value, user.GetAccountId(), user.Identity?.Name ?? "Personal", ct);

        if (!await db.CampaignEntities.AnyAsync(c => c.Id == campaignId && c.AccountId == accountId, ct))
            return Results.NotFound();

        var rows = await ShortLynx.Services.Analytics.LinkVisitQueries.LoadCampaignRowsAsync(db, campaignId, accountId, ct);
        var csv = ShortLynx.Services.Analytics.ClickBreakdownCsv.Format(
            ShortLynx.Services.Analytics.ClickAggregator.Summarize(rows));
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"campaign-{campaignId}-analytics.csv");
    })
    .RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
