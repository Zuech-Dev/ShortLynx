using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Extensions;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Admin;

// Regression coverage: same "no such table" gap as ShortLynx.Core (see
// ShortLynx.Tests.Api.DatabaseExtensionsTests), but Admin has its own AddShortLynxDatabase that wires
// ShortLynxDbContext via AddDbContextFactory instead of AddDbContext, so it needs its own
// EnsureSqliteSchemaCreated resolving IDbContextFactory<ShortLynxDbContext> rather than the context
// directly.
public class ServiceExtensionsSqliteSchemaTests
{
    [Fact]
    public async Task EnsureSqliteSchemaCreated_CreatesSchema_OnFreshSqliteDatabase()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "sqlite" })
            .Build();

        ServiceExtensions.EnsureSqliteSchemaCreated(provider, configuration);

        await using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(await verify.ShortCodeEntities.ToListAsync());
    }

    [Fact]
    public void EnsureSqliteSchemaCreated_NoOp_ForPostgresProvider()
    {
        // Deliberately no IDbContextFactory<ShortLynxDbContext> registered — if the sqlite-only guard
        // clause didn't short-circuit first, resolving it here would throw.
        using var provider = new ServiceCollection().BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "postgresql" })
            .Build();

        ServiceExtensions.EnsureSqliteSchemaCreated(provider, configuration);
    }

    [Fact]
    public void EnsureSqliteSchemaCreated_TreatsMissingProvider_AsSqlite()
    {
        // AddShortLynxDatabase defaults an unset Database:Provider to "sqlite" (see ServiceExtensions
        // line ~31); this guard must match that default rather than only recognizing an explicit value.
        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder().Build(); // no Database:Provider key at all

        ServiceExtensions.EnsureSqliteSchemaCreated(provider, configuration);

        using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(verify.ShortCodeEntities.ToList());
        connection.Dispose();
    }
}
