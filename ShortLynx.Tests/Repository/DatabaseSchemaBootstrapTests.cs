using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Repository;

// Regression coverage for a bug where `dotnet run` against a brand-new SQLite .db file failed
// immediately with "no such table" for every entity: SQLite intentionally has no migrations project
// (see CLAUDE.md), so nothing built its schema outside test factories hand-rolling their own
// EnsureCreated() call (see ApiFactory, AdminFactory). EnsureSqliteSchemaCreated closes that gap and
// is shared by Core, Admin, and Web so the sqlite-detection logic can't drift between them again.
public class DatabaseSchemaBootstrapTests
{
    [Fact]
    public async Task EnsureSqliteSchemaCreated_CreatesSchema_ViaScopedDbContext()
    {
        // Core and Web register ShortLynxDbContext directly via AddDbContext.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ShortLynxDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "sqlite" })
            .Build();

        ShortLynx.Repository.DatabaseSchemaBootstrap.EnsureSqliteSchemaCreated(provider, configuration);

        // A query against a table that was never created throws SqliteException ("no such table")
        // rather than returning — this is the exact failure the bug report hit at startup.
        await using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(await verify.ShortCodeEntities.ToListAsync());
    }

    [Fact]
    public async Task EnsureSqliteSchemaCreated_CreatesSchema_ViaDbContextFactoryRegistration()
    {
        // Admin registers ShortLynxDbContext through AddDbContextFactory plus a scoped forwarder
        // (see ShortLynx.Admin.Extensions.ServiceExtensions.AddShortLynxDatabase) rather than AddDbContext
        // directly — the shared bootstrap must work against that shape too.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(connection));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "sqlite" })
            .Build();

        ShortLynx.Repository.DatabaseSchemaBootstrap.EnsureSqliteSchemaCreated(provider, configuration);

        await using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(await verify.ShortCodeEntities.ToListAsync());
    }

    [Fact]
    public void EnsureSqliteSchemaCreated_NoOp_ForPostgresProvider()
    {
        // Deliberately no ShortLynxDbContext registered — if the sqlite-only guard clause didn't
        // short-circuit first, resolving it here would throw.
        using var provider = new ServiceCollection().BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "postgresql" })
            .Build();

        ShortLynx.Repository.DatabaseSchemaBootstrap.EnsureSqliteSchemaCreated(provider, configuration);
    }

    [Fact]
    public async Task EnsureSqliteSchemaCreated_TreatsMissingProvider_AsSqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ShortLynxDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder().Build(); // no Database:Provider key at all

        ShortLynx.Repository.DatabaseSchemaBootstrap.EnsureSqliteSchemaCreated(provider, configuration);

        await using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(await verify.ShortCodeEntities.ToListAsync());
    }
}
