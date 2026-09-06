using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Web.Extensions;

namespace ShortLynx.Tests.Web;

// Regression coverage: same "no such table" gap as ShortLynx.Core (see
// ShortLynx.Tests.Api.DatabaseExtensionsTests) — ShortLynx.Web is the only app that serves `/{code}`
// redirects (see CLAUDE.md), so a fresh SQLite deploy failing at the redirect path is the worst case
// of this bug.
public class ServiceExtensionsSqliteSchemaTests
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

        ServiceExtensions.EnsureSqliteSchemaCreated(provider, configuration);

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

        ServiceExtensions.EnsureSqliteSchemaCreated(provider, configuration);
    }
}
