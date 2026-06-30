using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ShortLynx.Admin;

namespace ShortLynx.Tests.Admin;

// Hosts the Admin app (Razor Pages + Blazor Server) in-process for route/middleware integration tests.
// Forces the SQLite provider via env vars (read eagerly by AddShortLynxDatabase) so the developer's
// Postgres user-secrets don't leak in. Most Admin coverage is bUnit/unit; this exists for the handful
// of things that only manifest at the routing/auth layer (e.g. a page's @page route vs. its form action).
public sealed class AdminFactory : WebApplicationFactory<AdminEntryPoint>
{
    public AdminFactory()
    {
        Environment.SetEnvironmentVariable("Database__Provider", "sqlite");
        Environment.SetEnvironmentVariable("Database__ConnectionString", "DataSource=:memory:");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey:HmacSecret"] = "test-hmac-secret-at-least-32-chars!",
                ["Email:Mode"] = "Log",
            });
        });
    }
}
