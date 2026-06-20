using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;
using ShortLynx.Services.Redirect;
using ShortLynx.Services.Visits;
using ShortLynx.Web.RateLimit;

namespace ShortLynx.Web.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddShortLynxDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"]
                    ?? throw new InvalidOperationException("Database:Provider is required.");
        var connectionString = configuration["Database:ConnectionString"]
                            ?? throw new InvalidOperationException("Database:ConnectionString is required.");

        switch (provider.ToLowerInvariant())
        {
            case "postgresql":
                services.AddDbContext<ShortLynxDbContext>(o =>
                    o.UseNpgsql(connectionString, x => x.MigrationsAssembly("ShortLynx.Data.PostgreSql")));
                services.AddScoped<IDbOperations, PostgresDbOperations>();
                break;
            case "sqlite":
                services.AddDbContext<ShortLynxDbContext>(o =>
                    o.UseSqlite(connectionString, x => x.MigrationsAssembly("ShortLynx.Data.Sqlite")));
                services.AddScoped<IDbOperations, EfCoreDbOperations>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider: {provider}");
        }

        return services;
    }

    public static IServiceCollection AddShortLynxRedirect(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedirectOptions>(configuration.GetSection("Redirect"));

        // Cap the cache so a flood of distinct codes can't grow it without bound (each entry has Size 1).
        var redirectOpts = configuration.GetSection("Redirect").Get<RedirectOptions>() ?? new RedirectOptions();
        services.AddMemoryCache(o => o.SizeLimit = redirectOpts.CacheSizeLimit);
        services.AddScoped<IRedirectService, RedirectService>();

        // Visit event pipeline: singleton sink shared between request handlers and the background writer.
        // BackgroundVisitWriter resolves IDbOperations per-flush via IServiceScopeFactory (avoids
        // the singleton-capturing-scoped-service anti-pattern).
        services.Configure<VisitSinkOptions>(configuration.GetSection("VisitSink"));
        services.AddSingleton<InMemoryVisitEventSink>();
        services.AddSingleton<IVisitEventSink>(sp => sp.GetRequiredService<InMemoryVisitEventSink>());
        services.AddHostedService<BackgroundVisitWriter>();

        return services;
    }

    public static IServiceCollection AddShortLynxRateLimiter(this IServiceCollection services, IConfiguration configuration)
    {
        var opts = configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(rl =>
        {
            rl.AddPolicy("redirect", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opts.PermitLimit,
                    Window = TimeSpan.FromSeconds(opts.WindowSeconds),
                    SegmentsPerWindow = 4,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                });
            });
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }
}
