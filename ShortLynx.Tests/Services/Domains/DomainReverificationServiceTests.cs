using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Domains;
using ShortLynx.Tests.Stubs;

namespace ShortLynx.Tests.Services.Domains;

public class DomainReverificationServiceTests
{
    // Wait for the background pass to take effect instead of a fixed delay, which flakes on slow CI.
    // Tolerates transient SQLite errors during the service's brief startup pass (it then idles ~24h).
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try { if (await condition()) return; }
            catch { /* DB momentarily busy during the pass; retry */ }
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task BackgroundService_RunsStartupPass_DemotingDriftedDomain()
    {
        // Shared in-memory SQLite for the whole provider.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var dns = new InMemoryDnsResolver();
        var services = new ServiceCollection();
        services.AddDbContext<ShortLynxDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton(Options.Create(new CustomDomainOptions()));
        services.AddSingleton<IDnsResolver>(dns);
        services.AddScoped<ICustomDomainService, CustomDomainService>();
        var provider = services.BuildServiceProvider();

        Guid domainId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            await db.Database.EnsureCreatedAsync();

            var account = new AccountEntity
            {
                Id = Guid.CreateVersion7(), Name = "acct",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            var domain = new CustomDomainEntity
            {
                Id = Guid.CreateVersion7(), AccountId = account.Id, Domain = "go.example.com",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
                VerificationStatus = DomainVerificationStatus.Verified,
                VerificationToken = "tok", VerifiedAt = DateTimeOffset.UtcNow,
            };
            db.AddRange(account, domain);
            await db.SaveChangesAsync();
            domainId = domain.Id;
        }

        // dns publishes nothing → the verified domain has drifted.
        var sut = new DomainReverificationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CustomDomainOptions()),
            NullLogger<DomainReverificationService>.Instance);

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        // The service runs a pass immediately at startup; poll until the drifted domain is demoted.
        await WaitUntilAsync(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var d = await db.CustomDomainEntities.FindAsync(domainId);
            return d!.VerificationStatus == DomainVerificationStatus.Failed;
        });
        await sut.StopAsync(cts.Token);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var stored = await db.CustomDomainEntities.FindAsync(domainId);
            Assert.Equal(DomainVerificationStatus.Failed, stored!.VerificationStatus);
            Assert.False(stored.IsActive);
        }

        await provider.DisposeAsync();
    }
}
