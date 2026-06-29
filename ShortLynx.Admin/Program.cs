using System.Security.Claims;
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
using ShortLynx.Services.Links;
using ShortLynx.Services.Qr;

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
