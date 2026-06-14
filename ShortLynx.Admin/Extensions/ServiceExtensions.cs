using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Email;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Admin.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddShortLynxDatabase(
        this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "sqlite";
        var connStr = configuration["Database:ConnectionString"] ?? "DataSource=shortlynx.db";

        services.AddDbContextFactory<ShortLynxDbContext>(opts =>
        {
            if (provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("postgres", StringComparison.OrdinalIgnoreCase))
                opts.UseNpgsql(connStr);
            else
                opts.UseSqlite(connStr);
        });

        // AddDbContextFactory only registers the factory (for Blazor components). Scoped services
        // — MagicLinkService, ApiKeyService, EfCoreDbOperations — need an injectable scoped
        // DbContext too, so create one from the factory.
        services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        services.AddScoped<IDbOperations, EfCoreDbOperations>();

        return services;
    }

    public static IServiceCollection AddShortLynxServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection("ApiKey"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.HmacSecret),
                "ApiKey:HmacSecret is required.")
            .Validate(o => o.HmacSecret != ApiKeyOptions.DefaultPlaceholderSecret,
                "ApiKey:HmacSecret must be changed from the default placeholder value.")
            .Validate(o => string.IsNullOrEmpty(o.HmacSecret) || o.HmacSecret.Length >= 32,
                "ApiKey:HmacSecret must be at least 32 characters.")
            .ValidateOnStart();
        services.Configure<MagicLinkOptions>(configuration.GetSection("MagicLink"));
        services.Configure<SmtpEmailOptions>(configuration.GetSection("Email"));
        services.Configure<AdminOptions>(configuration.GetSection("Admin"));

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();

        return services;
    }

    public static IServiceCollection AddShortLynxAuth(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(opts =>
            {
                opts.Cookie.Name = ".shortlynx.admin";
                opts.Cookie.HttpOnly = true;
                opts.Cookie.SameSite = SameSiteMode.Lax;
                opts.LoginPath = "/auth/login";
                opts.LogoutPath = "/auth/logout";
                opts.SlidingExpiration = true;
                opts.ExpireTimeSpan = TimeSpan.FromDays(7);
            });

        // Cross-tenant pages (user list, global totals) require the IsAdmin claim set at sign-in.
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AdminClaims.SuperAdminPolicy, p =>
                p.RequireClaim(AdminClaims.IsAdmin, "true"));
        });

        return services;
    }
}
