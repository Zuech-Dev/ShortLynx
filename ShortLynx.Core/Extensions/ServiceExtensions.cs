using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using ShortLynx.Core.RateLimit;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Auth;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Email;
using ShortLynx.Services.Links;
using ShortLynx.Services.MagicLinks;
using ShortLynx.Core.Options;
using ShortLynx.Services.Qr;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.UrlValidation;
using ShortLynx.Services.Users;
using ShortLynx.Services.Visits;

namespace ShortLynx.Core.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddShortLynxServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection("ApiKey"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.HmacSecret),
                "ApiKey:HmacSecret is required.")
            .Validate(o => o.HmacSecret != ApiKeyOptions.DefaultPlaceholderSecret,
                "ApiKey:HmacSecret must be changed from the default placeholder value.")
            .Validate(o => string.IsNullOrEmpty(o.HmacSecret) || o.HmacSecret.Length >= 32,
                "ApiKey:HmacSecret must be at least 32 characters.")
            .Validate(o => string.IsNullOrEmpty(o.AdminSecret) || o.AdminSecret!.Length >= 16,
                "ApiKey:AdminSecret must be at least 16 characters when set.")
            .ValidateOnStart();
        services.Configure<ShortCodeOptions>(configuration.GetSection("ShortCode"));
        services.Configure<UrlValidationOptions>(configuration.GetSection("UrlValidation"));
        services.Configure<MagicLinkOptions>(configuration.GetSection("MagicLink"));
        services.Configure<VisitSinkOptions>(configuration.GetSection("VisitSink"));
        services.Configure<SmtpEmailOptions>(configuration.GetSection("Email"));
        services.Configure<CustomDomainOptions>(configuration.GetSection("CustomDomain"));

        services.AddShortLynxEmail(configuration);
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IShortCodeGenerator, HashBase62Generator>();
        services.AddSingleton<IUrlValidationService, UrlValidationService>();
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();
        services.AddScoped<ICustomDomainService, CustomDomainService>();
        services.AddSingleton<IDnsResolver, DnsClientResolver>();
        services.AddHostedService<DomainReverificationService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.Configure<LinkUrlOptions>(configuration.GetSection("Links"));

        // User sessions (magic-link → JWT + refresh) for bring-your-own-frontend clients.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(o => o.IsValid, "Jwt:SigningKey is required, must differ from the placeholder, and be 32+ chars.")
            .ValidateOnStart();
        services.Configure<AccessControlOptions>(configuration.GetSection("Admin"));
        services.AddScoped<IUserSessionService, UserSessionService>();

        services.AddSingleton<InMemoryVisitEventSink>();
        services.AddSingleton<IVisitEventSink>(sp => sp.GetRequiredService<InMemoryVisitEventSink>());
        services.AddHostedService<BackgroundVisitWriter>();

        return services;
    }

    /// <summary>
    /// Per-IP rate limiting for the sensitive unauthenticated/admin endpoints. Without this the Core
    /// API has no throttling, so the magic-link endpoint can be abused as a spam relay and the
    /// admin-secret endpoint can be brute-forced.
    /// </summary>
    public static IServiceCollection AddShortLynxRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));

        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Options are resolved per-request (not captured at registration) so the final merged
            // configuration is honoured — important for tests that override limits.
            rl.AddPolicy(RateLimitPolicies.MagicLinks, ctx =>
            {
                var o = ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = o.MagicLinkPermitLimit,
                    Window = TimeSpan.FromSeconds(o.MagicLinkWindowSeconds),
                    QueueLimit = 0,
                });
            });

            rl.AddPolicy(RateLimitPolicies.ApiKeys, ctx =>
            {
                var o = ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = o.ApiKeyPermitLimit,
                    Window = TimeSpan.FromSeconds(o.ApiKeyWindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        return services;

        // NOTE: keys on the socket IP. Behind a reverse proxy this collapses to the proxy IP until
        // ForwardedHeaders is configured (tracked as follow-up M3).
        static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
