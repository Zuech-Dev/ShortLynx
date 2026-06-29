using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Infrastructure;

// Each TestDatabase wraps one open SQLite connection, giving a single shared in-memory DB.
// Call CreateContext() to get independent DbContext instances that all see the same data.
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestDatabase(SqliteConnection connection) => _connection = connection;

    internal static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var seed = new ShortLynxDbContext(Options(connection));
        await seed.Database.EnsureCreatedAsync();
        return new TestDatabase(connection);
    }

    internal ShortLynxDbContext CreateContext()
        => new(Options(_connection));

    private static DbContextOptions<ShortLynxDbContext> Options(SqliteConnection c)
        => new DbContextOptionsBuilder<ShortLynxDbContext>()
            .UseSqlite(c)
            .Options;

    public async ValueTask DisposeAsync()
        => await _connection.DisposeAsync();
}
