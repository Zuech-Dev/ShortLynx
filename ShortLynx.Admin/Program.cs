using Microsoft.AspNetCore.HttpOverrides;
using ShortLynx.Admin.Components;
using ShortLynx.Admin.Extensions;
using ShortLynx.Repository;

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
