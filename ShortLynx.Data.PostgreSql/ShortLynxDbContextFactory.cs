using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ShortLynx.Data.Context;

namespace ShortLynx.Data.PostgreSQL;

public class ShortLynxDbContextFactory : IDesignTimeDbContextFactory<ShortLynxDbContext>
{
    // Design-time factory used by `dotnet ef` (add migration, build the migrations bundle). Resolution
    // order: DATABASE_URL env (CI/containers) → appsettings.Development.json (local dev). Design-time
    // operations never open a connection, so when neither source is present — e.g. building the bundle
    // in the Docker image, where appsettings.Development.json is git-ignored and DATABASE_URL is unset —
    // a harmless placeholder lets the build proceed. The runtime `efbundle` is given the real
    // --connection, so the placeholder never reaches a live database.
    private const string DesignTimePlaceholderConnection =
        "Host=localhost;Database=shortlynx_designtime;Username=postgres;Password=postgres";

    public ShortLynxDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL");

        if (string.IsNullOrEmpty(connectionString))
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();
            connectionString = config["Database:ConnectionString"];
        }

        if (string.IsNullOrEmpty(connectionString))
            connectionString = DesignTimePlaceholderConnection;

        var options = new DbContextOptionsBuilder<ShortLynxDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("ShortLynx.Data.PostgreSql"))
            .Options;

        return new ShortLynxDbContext(options);
    }
}