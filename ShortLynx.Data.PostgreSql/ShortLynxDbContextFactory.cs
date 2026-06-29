using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ShortLynx.Data.Context;

namespace ShortLynx.Data.PostgreSQL;

public class ShortLynxDbContextFactory : IDesignTimeDbContextFactory<ShortLynxDbContext>
{
    public ShortLynxDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrEmpty(connectionString))
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Development.json", optional: false)
                .Build();

            connectionString = config["Database:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "Set DATABASE_URL or Database:ConnectionString in appsettings.Development.json.");
        }

        var options = new DbContextOptionsBuilder<ShortLynxDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("ShortLynx.Data.PostgreSql"))
            .Options;

        return new ShortLynxDbContext(options);
    }
}