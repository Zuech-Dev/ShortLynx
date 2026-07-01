using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Auth;
using ShortLynx.Services.Campaigns;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Email;
using ShortLynx.Services.Links;
using ShortLynx.Services.MagicLinks;
using ShortLynx.Services.ShortCodes;
using ShortLynx.Services.UrlValidation;

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
                opts.UseNpgsql(connStr, x => x.MigrationsAssembly("ShortLynx.Data.PostgreSql"));
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
        services.Configure<AccessControlOptions>(configuration.GetSection("Admin"));
        services.Configure<DashboardOptions>(configuration.GetSection("Dashboard"));
        services.Configure<ShortCodeOptions>(configuration.GetSection("ShortCode"));
        services.Configure<UrlValidationOptions>(configuration.GetSection("UrlValidation"));
        services.Configure<CustomDomainOptions>(configuration.GetSection("CustomDomain"));

        services.AddShortLynxEmail(configuration);
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IMagicLinkService, MagicLinkService>();
        // Link creation from the dashboard (user-owned links).
        // Open-source default: unlimited at every tier, so self-hosting is fully featured and free.
        // A hosted deployment replaces this with a billing-backed policy (outside this repo).
        services.AddSingleton<IEntitlements, UnlimitedEntitlements>();
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<IShortCodeGenerator, HashBase62Generator>();
        services.AddSingleton<IUrlValidationService, UrlValidationService>();
        // Custom domains: management + DNS-TXT verification.
        services.AddScoped<ICustomDomainService, CustomDomainService>();
        services.AddSingleton<IDnsResolver, DnsClientResolver>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ShortLynx.Services.Users.IUserAdminService, ShortLynx.Services.Users.UserAdminService>();
        services.AddSingleton<ShortLynx.Services.Qr.IQrCodeService, ShortLynx.Services.Qr.QrCodeService>();

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
                // Always require HTTPS for the session cookie (works in dev over localhost HTTPS, and
                // in prod once UseForwardedHeaders surfaces the original scheme behind TLS termination).
                opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;
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
