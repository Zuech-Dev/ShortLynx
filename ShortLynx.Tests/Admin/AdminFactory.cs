using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Admin;

// Hosts the Admin app (Razor Pages + Blazor Server) in-process for route/middleware integration tests.
// Forces the SQLite provider via env vars (read eagerly by AddShortLynxDatabase) so the developer's
// Postgres user-secrets don't leak in. Most Admin coverage is bUnit/unit; this exists for the handful
// of things that only manifest at the routing/auth layer (e.g. a page's @page route vs. its form action)
// or that need a real request round-trip against the database (e.g. a webhook that reads/writes rows).
//
// A single SqliteConnection is held open for the factory's lifetime (mirroring ApiFactory) so every
// DbContext created during a test shares the same in-process database — a bare ":memory:" connection
// string otherwise hands each new connection its own empty, schema-less database.
public sealed class AdminFactory : WebApplicationFactory<AdminEntryPoint>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public AdminFactory()
    {
        Environment.SetEnvironmentVariable("Database__Provider", "sqlite");
        Environment.SetEnvironmentVariable("Database__ConnectionString", "DataSource=:memory:");

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey:HmacSecret"] = "test-hmac-secret-at-least-32-chars!",
                ["Email:Mode"] = "Log",
                ["Threads:AppSecret"] = "test-meta-app-secret",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the factory-backed DbContext registration with one pinned to the shared,
            // already-open connection, so requests made through HttpClient see data seeded beforehand.
            var toRemove = services
                .Where(d => d.ServiceType == typeof(IDbContextFactory<ShortLynxDbContext>))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);
            services.AddDbContextFactory<ShortLynxDbContext>(opts => opts.UseSqlite(_connection));

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
