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
}
