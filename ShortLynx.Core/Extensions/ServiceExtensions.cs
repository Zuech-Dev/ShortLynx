using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ShortLynx.Core.RateLimit;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Auth;
using ShortLynx.Services.Campaigns;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Entitlements;
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
        // Open-source default: unlimited at every tier, so self-hosting is fully featured and free.
        // A hosted deployment replaces this with a billing-backed policy (outside this repo).
        services.AddSingleton<IEntitlements, UnlimitedEntitlements>();

        // Token encryption for social connections. The key ring is persisted in the DATABASE and the
        // application name is shared, so Core and Admin use one ring: tokens protected by either app are
        // readable by the other, and keys survive redeploys with no mounted volume.
        services.AddDataProtection()
            .SetApplicationName("ShortLynx")
            .PersistKeysToDbContext<ShortLynx.Data.Context.ShortLynxDbContext>();
        services.AddSingleton<ShortLynx.Services.Social.ITokenProtector, ShortLynx.Services.Social.DataProtectionTokenProtector>();

        // Social connectors (one per platform, typed HttpClients) + the account-scoped connection service.
        services.AddHttpClient<ShortLynx.Services.Social.ISocialConnector, ShortLynx.Services.Social.BlueskyConnector>();
        services.AddHttpClient<ShortLynx.Services.Social.ISocialConnector, ShortLynx.Services.Social.MastodonConnector>();
        // OAuth-only platforms (Threads, Reddit) — the authorize/callback endpoints live on Admin, but
        // Core's /me/links/{id}/publish and the metrics puller both run ISocialConnector over any
        // connected platform, so they're part of this collection here too (concrete typed clients,
        // bridged; a single IOAuthSocialConnector registration can't hold two implementations).
        services.Configure<ShortLynx.Services.Social.ThreadsOptions>(configuration.GetSection("Threads"));
        services.AddHttpClient<ShortLynx.Services.Social.ThreadsConnector>();
        services.AddScoped<ShortLynx.Services.Social.ISocialConnector>(
            sp => sp.GetRequiredService<ShortLynx.Services.Social.ThreadsConnector>());
        services.Configure<ShortLynx.Services.Social.RedditOptions>(configuration.GetSection("Reddit"));
        services.AddHttpClient<ShortLynx.Services.Social.RedditConnector>();
        services.AddScoped<ShortLynx.Services.Social.ISocialConnector>(
            sp => sp.GetRequiredService<ShortLynx.Services.Social.RedditConnector>());
        services.AddScoped<ShortLynx.Services.Social.ISocialConnectionService, ShortLynx.Services.Social.SocialConnectionService>();
        services.AddScoped<ShortLynx.Services.Social.ISocialPublishService, ShortLynx.Services.Social.SocialPublishService>();
        services.Configure<ShortLynx.Services.Social.SocialMetricsOptions>(configuration.GetSection("SocialMetrics"));
        services.AddScoped<ShortLynx.Services.Social.ISocialMetricsService, ShortLynx.Services.Social.SocialMetricsService>();
        services.AddHostedService<ShortLynx.Services.Social.SocialMetricsBackgroundService>();
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<ICampaignService, CampaignService>();
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

        // Pure reducers the writer uses to derive low-entropy dimensions from raw signals at ingest.
        services.AddSingleton<ShortLynx.Services.Analytics.IUserAgentParser, ShortLynx.Services.Analytics.UserAgentParser>();
        services.AddSingleton<ShortLynx.Services.Analytics.IReferrerReducer, ShortLynx.Services.Analytics.ReferrerReducer>();
        services.AddSingleton<ShortLynx.Services.Analytics.ILanguageReducer, ShortLynx.Services.Analytics.LanguageReducer>();
        // GeoLite2 resolver when a database is configured (country + timezone only); no-op otherwise.
        services.AddSingleton<ShortLynx.Services.Analytics.IGeoIpResolver>(sp =>
        {
            var path = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VisitSinkOptions>>().Value.GeoIpDatabasePath;
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("GeoIp");
            if (string.IsNullOrWhiteSpace(path))
            {
                log.LogInformation("GeoIP resolution disabled (VisitSink:GeoIpDatabasePath not set).");
                return new ShortLynx.Services.Analytics.NullGeoIpResolver();
            }
            if (!File.Exists(path))
            {
                // Loud, not silent: a set-but-missing path is a misconfiguration (or the startup
                // fetch failed), and the only symptom would otherwise be null country columns.
                log.LogWarning("GeoIP resolution disabled: VisitSink:GeoIpDatabasePath is set but no file exists at {Path}.", path);
                return new ShortLynx.Services.Analytics.NullGeoIpResolver();
            }
            log.LogInformation("GeoIP resolution enabled (GeoLite2 database at {Path}).", path);
            return new ShortLynx.Services.Analytics.MaxMindGeoIpResolver(path);
        });
        services.AddSingleton<InMemoryVisitEventSink>();
        services.AddSingleton<IVisitEventSink>(sp => sp.GetRequiredService<InMemoryVisitEventSink>());
        services.AddHostedService<BackgroundVisitWriter>();
        // Nightly retention prune; no-op unless VisitSink:AnalyticsRetentionDays is set.
        services.AddHostedService<VisitRetentionService>();

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

            rl.AddPolicy(RateLimitPolicies.Refresh, ctx =>
            {
                var o = ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = o.RefreshPermitLimit,
                    Window = TimeSpan.FromSeconds(o.RefreshWindowSeconds),
                    QueueLimit = 0,
                });
            });
        });

        return services;

        // Keys on RemoteIpAddress, which the ForwardedHeaders middleware (configured in Program.cs to
        // trust Railway's edge hop) rewrites to the real client IP before this runs — so partitioning
        // is per-client, not per-proxy-connection.
        static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
