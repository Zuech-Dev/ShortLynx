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
using ShortLynx.Services.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddShortLynxLogging();

// Error tracking, opt-in — unset means exactly today's behavior, no outbound telemetry at all. See
// ObservabilityExtensions.cs's doc comment for why UseSentry() is called here directly rather than
// from that shared method (it needs the ASP.NET Core shared framework, which only a Web SDK app has).
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        o.Environment = builder.Environment.EnvironmentName;
        // Explicit, even though it's the SDK default: no request bodies, full headers, or user claims
        // leave the process. Belt-and-suspenders on top of ScrubSensitiveData, matching how VisitSink
        // treats IP hashing (pepper AND daily rotation, not just one).
        o.SendDefaultPii = false;
        o.SetBeforeSend((@event, _) => ObservabilityExtensions.ScrubSensitiveData(@event));
    });
}

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
