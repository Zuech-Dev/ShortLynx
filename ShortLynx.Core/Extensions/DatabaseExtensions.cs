using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;

namespace ShortLynx.Core.Extensions;

public static class DatabaseExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddShortLynxDatabase(IConfiguration configuration)
        {
            var provider = configuration["Database:Provider"]
                        ?? throw new InvalidOperationException("Database:Provider is required.");
            var connectionString = configuration["Database:ConnectionString"]
                                ?? throw new InvalidOperationException("Database:ConnectionString is required.");

            switch (provider.ToLowerInvariant())
            {
                case "postgresql":
                    services.AddDbContext<ShortLynxDbContext>(o =>
                                                                  o.UseNpgsql(connectionString,
                                                                              x => x.MigrationsAssembly("ShortLynx.Data.PostgreSql")));
                    services.AddScoped<IDbOperations, PostgresDbOperations>();
                    break;
                case "sqlite":
                    services.AddDbContext<ShortLynxDbContext>(o =>
                                                                  o.UseSqlite(connectionString,
                                                                              x => x.MigrationsAssembly("ShortLynx.Data.Sqlite")));
                    services.AddScoped<IDbOperations, EfCoreDbOperations>();
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported provider: {provider}");
            }

            return services;
        }
    }

    /// <summary>
    /// SQLite has no dedicated migrations project (see CLAUDE.md's Architecture section), so
    /// <c>MigrationsAssembly("ShortLynx.Data.Sqlite")</c> above never resolves any migrations for it —
    /// EnsureCreated is the only schema-creation path for that provider, mirroring what
    /// <c>ApiFactory</c> already does for the test host. It's a cheap no-op once the tables exist, so
    /// this is safe to call unconditionally at startup. Postgres is untouched: it keeps using real EF
    /// migrations via <see cref="DatabaseMigrationGuard"/>.
    /// </summary>
    public static void EnsureSqliteSchemaCreated(IServiceProvider services, IConfiguration configuration)
    {
        if (!string.Equals(configuration["Database:Provider"], "sqlite", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>().Database.EnsureCreated();
    }
}
