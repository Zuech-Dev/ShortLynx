using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Extensions;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Api;

// Regression coverage for a bug where `dotnet run` against a brand-new SQLite .db file failed
// immediately with "no such table" for every entity: SQLite intentionally has no migrations project
// (see CLAUDE.md), so the MigrationsAssembly configured in AddShortLynxDatabase never resolves any
// migrations to apply, and nothing else created the schema outside of test factories hand-rolling
// their own EnsureCreated() call (see ApiFactory). EnsureSqliteSchemaCreated closes that gap.
public class DatabaseExtensionsTests
{
    [Fact]
    public async Task EnsureSqliteSchemaCreated_CreatesSchema_OnFreshSqliteDatabase()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ShortLynxDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "sqlite" })
            .Build();

        DatabaseExtensions.EnsureSqliteSchemaCreated(provider, configuration);

        // A query against a table that was never created throws SqliteException ("no such table")
        // rather than returning — this is the exact failure the bug report hit at startup.
        await using var verify = new ShortLynxDbContext(
            new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(connection).Options);
        Assert.Empty(await verify.ShortCodeEntities.ToListAsync());
    }

    [Fact]
    public void EnsureSqliteSchemaCreated_NoOp_ForNonSqliteProvider()
    {
        // Deliberately no ShortLynxDbContext registered — if the sqlite-only guard clause didn't
        // short-circuit first, resolving it here would throw.
        using var provider = new ServiceCollection().BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "postgresql" })
            .Build();

        DatabaseExtensions.EnsureSqliteSchemaCreated(provider, configuration);
    }
}
