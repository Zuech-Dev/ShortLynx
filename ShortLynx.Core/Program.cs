using Microsoft.AspNetCore.HttpOverrides;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Extensions;
using ShortLynx.Repository;

var builder = WebApplication.CreateBuilder(args);

// Honour X-Forwarded-* from the hosting proxy (Railway) so client IP (rate limiting) and the original
// scheme (HTTPS redirect) are correct. KnownProxies/Networks are cleared because the proxy IP is
// dynamic and the app is only reachable through Railway's edge.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxServices(builder.Configuration);
builder.Services.AddShortLynxRateLimiting(builder.Configuration);

builder.Services
    .AddAuthentication(ApiKeyAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
        ApiKeyAuthHandler.SchemeName, null);
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Dev-only guard: fail fast at startup if the database is behind the migrations, so schema drift
// (a generated-but-unapplied migration) surfaces here instead of as a cryptic query-time error like
// "column does not exist". Resolve with: dotnet ef database update.
if (app.Environment.IsDevelopment())
    DatabaseMigrationGuard.ThrowIfPending(app.Services);

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();   // RFC 7807 ProblemDetails responses
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
