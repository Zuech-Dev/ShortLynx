using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.Domains;

/// <summary>
/// Periodically re-checks verified custom domains' TXT records and demotes any that no longer match,
/// so a link pinned to a domain the user no longer controls stops resolving. Runs one pass at startup
/// then every <see cref="CustomDomainOptions.ReverifyIntervalMinutes"/>.
/// </summary>
public sealed class DomainReverificationService(
    IServiceScopeFactory scopeFactory,
    IOptions<CustomDomainOptions> options,
    ILogger<DomainReverificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.ReverifyIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICustomDomainService>();
                var demoted = await svc.RecheckVerifiedAsync(stoppingToken);
                if (demoted > 0)
                    logger.LogWarning(
                        "Domain re-verification demoted {Count} domain(s) whose TXT record no longer matches.",
                        demoted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Domain re-verification pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
