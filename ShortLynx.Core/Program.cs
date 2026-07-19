using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Extensions;
using ShortLynx.Repository;
using ShortLynx.Services.Auth;

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

builder.Services.AddShortLynxDatabase(builder.Configuration);
builder.Services.AddShortLynxServices(builder.Configuration);
builder.Services.AddShortLynxRateLimiting(builder.Configuration);

builder.Services
    .AddAuthentication(ApiKeyAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
        ApiKeyAuthHandler.SchemeName, null)
    // User-session bearer scheme: reads the JWT from the Authorization header or the access cookie.
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

// Configure the bearer options from JwtOptions at runtime so the signing key matches the issuer's
// (binding eagerly at build time can miss later-merged configuration, e.g. in tests).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtAccessor) =>
    {
        var jwt = jwtAccessor.Value;
        bearer.MapInboundClaims = false; // keep raw claim names (sub, account_id, role, …)
        var keyMaterial = string.IsNullOrEmpty(jwt.SigningKey) ? new string('0', 32) : jwt.SigningKey;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyMaterial)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token) &&
                    ctx.Request.Cookies.TryGetValue(jwt.AccessCookieName, out var cookie))
                    ctx.Token = cookie;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
    // Platform super-admins (is_admin claim) gate the cross-tenant /admin/* surface.
    options.AddPolicy(AuthorizationPolicies.SuperAdmin, p => p.RequireClaim(JwtClaims.IsAdmin, "true")));

// CORS for bring-your-own-frontend clients. Configure Cors:AllowedOrigins (exact origins) to enable
// cross-origin access; credentials are allowed so cookie sessions work. Empty ⇒ same-origin only.
const string CorsPolicy = "frontend";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
{
    if (allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

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
app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseMiddleware<ShortLynx.Core.Auth.CsrfCookieMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
