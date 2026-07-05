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

// ── Threads (Meta) OAuth + Meta App Review webhooks ─────────────────────────────────────────────
// These four routes exist because Meta's Threads app dashboard requires exact, working URLs for
// them before it will accept an App Review submission — see docs/META_APP_SETUP.md.
const string ThreadsOAuthStateCookieName = "sl_threads_oauth_state";
const string ThreadsOAuthStateCookiePurpose = "ShortLynx.ThreadsOAuthState";

// "Connect Threads" entry point: mint a random anti-CSRF state, stash it in a short-lived cookie
// (tamper-evident via DataProtection, not just base64), and send the browser to Meta's consent screen.
app.MapGet("/social/threads/authorize", (
        HttpContext http,
        IOAuthSocialConnector connector,
        IOptions<ThreadsOptions> threadsOptions,
        IDataProtectionProvider dataProtection) =>
    {
        // Unconfigured deployments (no Meta app yet — most self-hosters) must fail here with a clear
        // message, not send the browser to Meta with an empty client_id, which Meta answers with an
        // unhelpful generic "unknown error" page.
        if (string.IsNullOrWhiteSpace(threadsOptions.Value.AppId) ||
            string.IsNullOrWhiteSpace(threadsOptions.Value.AppSecret))
            return Results.Redirect("/social?threadsError=not_configured");

        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var protector = dataProtection.CreateProtector(ThreadsOAuthStateCookiePurpose);
        http.Response.Cookies.Append(ThreadsOAuthStateCookieName, protector.Protect(state), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(10),
        });

        return Results.Redirect(connector.BuildAuthorizeUrl(threadsOptions.Value.RedirectUri, state));
    })
    .RequireAuthorization();

// Where Meta sends the browser back after the user approves (or denies) access. Must exactly match
// Meta:RedirectUri configured in the app dashboard.
app.MapGet("/social/threads/callback", async (
        HttpContext http, string? code, string? state, string? error,
        IOAuthSocialConnector connector,
        IOptions<ThreadsOptions> threadsOptions,
        IDataProtectionProvider dataProtection,
        IDbContextFactory<ShortLynxDbContext> dbFactory,
        ISocialConnectionService socialConnections,
        ClaimsPrincipal user,
        CancellationToken ct) =>
    {
        if (!string.IsNullOrEmpty(error))
            return Results.Redirect($"/social?threadsError={Uri.EscapeDataString(error)}");

        var cookieValue = http.Request.Cookies[ThreadsOAuthStateCookieName];
        http.Response.Cookies.Delete(ThreadsOAuthStateCookieName); // single use either way

        if (string.IsNullOrEmpty(cookieValue) || string.IsNullOrEmpty(state))
            return Results.Redirect("/social?threadsError=missing_state");

        string expectedState;
        try
        {
            expectedState = dataProtection.CreateProtector(ThreadsOAuthStateCookiePurpose).Unprotect(cookieValue);
        }
        catch (CryptographicException)
        {
            return Results.Redirect("/social?threadsError=invalid_state");
        }

        // Anti-CSRF: the value returned by Meta must match the one this same browser was handed at
        // /authorize — otherwise this could be a crafted callback URL opened in a victim's browser.
        if (!string.Equals(expectedState, state, StringComparison.Ordinal))
            return Results.Redirect("/social?threadsError=state_mismatch");

        if (string.IsNullOrEmpty(code))
            return Results.Redirect("/social?threadsError=missing_code");

        var userId = user.GetUserId();
        if (userId is null) return Results.Unauthorized();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accountId = await AccountResolver.ResolveAccountIdAsync(
            db, userId.Value, user.GetAccountId(), user.Identity?.Name ?? "Personal", ct);

        try
        {
            var identity = await connector.ExchangeAuthorizationCodeAsync(code, threadsOptions.Value.RedirectUri, ct);
            await socialConnections.ConnectFromIdentityAsync(
                accountId, userId.Value, SocialPlatform.Threads, identity, instanceUrl: null, ct);
        }
        catch (ArgumentException ex)
        {
            return Results.Redirect($"/social?threadsError={Uri.EscapeDataString(ex.Message)}");
        }
        catch (EntitlementException)
        {
            return Results.Redirect("/social?threadsError=plan");
        }

        return Results.Redirect("/social?connected=threads");
    })
    .RequireAuthorization();

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
