using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
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
using ShortLynx.Services.Social;

var builder = WebApplication.CreateBuilder(args);

// Honour X-Forwarded-* from the hosting proxy (Railway) so the app sees the original HTTPS scheme
// (required for the Secure cookie + HTTPS redirect to work behind TLS termination).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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
        if (targetCode is null)
        {
            if (link.Mode != LinkMode.Anonymous) return Results.BadRequest("A code is required for user-attributed links.");
            targetCode = await db.ShortCodeEntities.Where(sc => sc.LinkId == linkId)
                .Select(sc => sc.Code).FirstOrDefaultAsync(ct);
        }
        else
        {
            var belongs = link.Mode == LinkMode.Anonymous
                ? await db.ShortCodeEntities.AnyAsync(sc => sc.LinkId == linkId && sc.Code == targetCode, ct)
                : await db.UserLinkCodeEntities.AnyAsync(c => c.LinkId == linkId && c.Code == targetCode, ct);
            if (!belongs) return Results.NotFound();
        }
        if (string.IsNullOrEmpty(targetCode)) return Results.NotFound();

        var url = await ShortUrlBuilder.BuildAsync(db, link, targetCode, dashboard.Value.PublicBaseUrl, ct);
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

// ── Social OAuth (Threads, Reddit) + Meta App Review webhooks ───────────────────────────────────
// The Threads routes exist because Meta's app dashboard requires exact, working URLs before it will
// accept an App Review submission (docs/META_APP_SETUP.md); Reddit mirrors the same registered-URL
// contract (docs/REDDIT_APP_SETUP.md). One shared flow: mint anti-CSRF state in a short-lived
// DataProtection-wrapped cookie, bounce to the platform's consent screen, verify state on return,
// exchange the code, and upsert the connection.
void MapSocialOAuth(string slug, SocialPlatform platform,
    Func<IServiceProvider, (string AppId, string AppSecret, string RedirectUri)> credentials)
{
    var cookieName = $"sl_{slug}_oauth_state";
    var cookiePurpose = $"ShortLynx.{platform}OAuthState";
    var errorParam = $"{slug}Error";

    app.MapGet($"/social/{slug}/authorize", (
            HttpContext http,
            IEnumerable<ISocialConnector> connectors,
            IDataProtectionProvider dataProtection) =>
        {
            // Unconfigured deployments (no platform app yet — most self-hosters) must fail here with a
            // clear message, not send the browser to the platform with an empty client_id, which is
            // answered with an unhelpful generic error page.
            var (appId, appSecret, redirectUri) = credentials(http.RequestServices);
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
                return Results.Redirect($"/social?{errorParam}=not_configured");

            var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var protector = dataProtection.CreateProtector(cookiePurpose);
            http.Response.Cookies.Append(cookieName, protector.Protect(state), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10),
            });

            return Results.Redirect(
                OAuthConnectorResolver.Require(connectors, platform).BuildAuthorizeUrl(redirectUri, state));
        })
        .RequireAuthorization();

    // Where the platform sends the browser back after the user approves (or denies) access. Must
    // exactly match the redirect URI registered in the platform's app settings.
    app.MapGet($"/social/{slug}/callback", async (
            HttpContext http, string? code, string? state, string? error,
            IEnumerable<ISocialConnector> connectors,
            IDataProtectionProvider dataProtection,
            IDbContextFactory<ShortLynxDbContext> dbFactory,
            ISocialConnectionService socialConnections,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrEmpty(error))
                return Results.Redirect($"/social?{errorParam}={Uri.EscapeDataString(error)}");

            var cookieValue = http.Request.Cookies[cookieName];
            http.Response.Cookies.Delete(cookieName); // single use either way

            if (string.IsNullOrEmpty(cookieValue) || string.IsNullOrEmpty(state))
                return Results.Redirect($"/social?{errorParam}=missing_state");

            string expectedState;
            try
            {
                expectedState = dataProtection.CreateProtector(cookiePurpose).Unprotect(cookieValue);
            }
            catch (CryptographicException)
            {
                return Results.Redirect($"/social?{errorParam}=invalid_state");
            }

            // Anti-CSRF: the value returned by the platform must match the one this same browser was
            // handed at /authorize — otherwise this could be a crafted callback URL in a victim's browser.
            if (!string.Equals(expectedState, state, StringComparison.Ordinal))
                return Results.Redirect($"/social?{errorParam}=state_mismatch");

            if (string.IsNullOrEmpty(code))
                return Results.Redirect($"/social?{errorParam}=missing_code");

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var roleCtx = await ShortLynx.Admin.Components.AccountRoleContext.ResolveAsync(db, user, ct);
            if (roleCtx is null) return Results.Unauthorized();
            // This endpoint *creates* a connection — same ManageResources gate as the Social page's
            // Connect handler, or a Viewer could complete the OAuth flow directly and bypass the UI gate.
            if (!roleCtx.CanManageResources)
                return Results.Redirect($"/social?{errorParam}=forbidden");

            try
            {
                var connector = OAuthConnectorResolver.Require(connectors, platform);
                var identity = await connector.ExchangeAuthorizationCodeAsync(
                    code, credentials(http.RequestServices).RedirectUri, ct);
                await socialConnections.ConnectFromIdentityAsync(
                    roleCtx.AccountId, roleCtx.UserId, platform, identity, instanceUrl: null, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.Redirect($"/social?{errorParam}={Uri.EscapeDataString(ex.Message)}");
            }
            catch (EntitlementException)
            {
                return Results.Redirect($"/social?{errorParam}=plan");
            }

            return Results.Redirect($"/social?connected={slug}");
        })
        .RequireAuthorization();
}

MapSocialOAuth("threads", SocialPlatform.Threads, sp =>
{
    var o = sp.GetRequiredService<IOptions<ThreadsOptions>>().Value;
    return (o.AppId, o.AppSecret, o.RedirectUri);
});
MapSocialOAuth("reddit", SocialPlatform.Reddit, sp =>
{
    var o = sp.GetRequiredService<IOptions<RedditOptions>>().Value;
    return (o.AppId, o.AppSecret, o.RedirectUri);
});

// Meta POSTs a signed_request here when a user removes ShortLynx from their Threads app settings.
// Unauthenticated by design — this is a server-to-server call from Meta, not a user's browser — so the
// HMAC verification is the only thing standing between this and anyone deleting anyone's connection.
app.MapPost("/webhooks/threads/deauthorize", async (
        HttpRequest request,
        IOptions<ThreadsOptions> threadsOptions,
        IDbContextFactory<ShortLynxDbContext> dbFactory,
        CancellationToken ct) =>
    {
        var form = await request.ReadFormAsync(ct);
        if (!MetaSignedRequestParser.TryParse(form["signed_request"], threadsOptions.Value.AppSecret, out var payload))
            return Results.BadRequest();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SocialConnectionEntities
            .Where(c => c.Platform == SocialPlatform.Threads && c.ExternalAccountId == payload!.UserId)
            .ExecuteDeleteAsync(ct);

        return Results.Ok();
    });

// Meta POSTs a signed_request here when a user requests deletion of their data via Meta's own UI
// (Settings → Apps and Websites). Must respond with the exact { url, confirmation_code } shape Meta's
// Data Deletion Callback spec requires.
app.MapPost("/webhooks/threads/delete", async (
        HttpRequest request,
        IOptions<ThreadsOptions> threadsOptions,
        IDbContextFactory<ShortLynxDbContext> dbFactory,
        CancellationToken ct) =>
    {
        var form = await request.ReadFormAsync(ct);
        if (!MetaSignedRequestParser.TryParse(form["signed_request"], threadsOptions.Value.AppSecret, out var payload))
            return Results.BadRequest();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SocialConnectionEntities
            .Where(c => c.Platform == SocialPlatform.Threads && c.ExternalAccountId == payload!.UserId)
            .ExecuteDeleteAsync(ct);

        var confirmationCode = Guid.NewGuid().ToString("N")[..16];
        return Results.Json(new
        {
            url = $"{request.Scheme}://{request.Host}/social/threads/delete-status?id={confirmationCode}",
            confirmation_code = confirmationCode,
        });
    });

// A generic confirmation page for the URL above. Intentionally stateless — by the time this URL could
// be visited, the deletion the confirmation_code refers to has already completed (or there was nothing
// to delete), so there's no per-code status to look up.
app.MapGet("/social/threads/delete-status", () => Results.Content(
    "<!doctype html><html><body><h1>Deletion complete</h1>" +
    "<p>Any ShortLynx data associated with your Threads account has been deleted.</p></body></html>",
    "text/html"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
