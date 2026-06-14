using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;
using ShortLynx.Services.Email;
using ShortLynx.Tests.Stubs;

namespace ShortLynx.Tests.Api;

// Custom factory that replaces the database with an in-memory SQLite instance.
// A single SqliteConnection is held open for the lifetime of the factory so that
// all DbContext instances within a test share the same in-process database.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public readonly InMemoryEmailSender EmailSender = new();

    public ApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey:HmacSecret"] = "test-hmac-secret-at-least-32-chars!",
                ["ApiKey:AdminSecret"] = "test-admin-secret-value",
                ["MagicLink:ConfirmationUrlBase"] = "https://test.example.com/auth/confirm",
                // High limits so normal tests don't trip the limiter; rate-limit tests override these.
                ["RateLimit:MagicLinkPermitLimit"] = "1000",
                ["RateLimit:ApiKeyPermitLimit"] = "1000",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove all existing DbContext and IDbOperations registrations added by AddShortLynxDatabase.
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ShortLynxDbContext>) ||
                    d.ServiceType == typeof(IDbOperations))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<ShortLynxDbContext>(opts => opts.UseSqlite(_connection));
            services.AddScoped<IDbOperations, EfCoreDbOperations>();

            // Replace SmtpEmailSender with an in-memory sender so no real SMTP is needed.
            var emailDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));
            if (emailDescriptor is not null) services.Remove(emailDescriptor);
            services.AddSingleton<IEmailSender>(EmailSender);

            // Ensure the schema is created before tests run.
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            db.Database.EnsureCreated();
        });
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }
}
