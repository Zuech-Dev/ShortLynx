using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;

namespace ShortLynx.Repository;

/// <summary>
/// Startup schema creation for SQLite — the provider has no dedicated migrations project (see
/// CLAUDE.md's Architecture section), so EnsureCreated is the only way its schema ever gets built.
/// Shared by Core, Admin, and Web (each has its own <c>AddShortLynxDatabase</c> wiring, but all three
/// register a scoped <see cref="ShortLynxDbContext"/>) so the sqlite-detection logic lives in one place
/// instead of drifting across copies. Postgres is untouched: it keeps using real EF migrations, guarded
/// by <see cref="DatabaseMigrationGuard"/>.
/// </summary>
public static class DatabaseSchemaBootstrap
{
    /// <summary>
    /// Calls <c>EnsureCreated()</c> when the resolved provider is sqlite (a cheap no-op once the tables
    /// exist, so safe to call unconditionally at startup); a no-op for any other provider. An unset
    /// <c>Database:Provider</c> is treated as sqlite, matching that key's default everywhere it's read.
    /// </summary>
    public static void EnsureSqliteSchemaCreated(IServiceProvider services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "sqlite";
        var isPostgres = provider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
                          provider.Equals("postgres", StringComparison.OrdinalIgnoreCase);
        if (isPostgres) return;

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>().Database.EnsureCreated();
    }
}
