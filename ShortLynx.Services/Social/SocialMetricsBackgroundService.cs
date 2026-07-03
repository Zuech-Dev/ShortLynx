using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.Social;

/// <summary>
/// Periodically pulls engagement metrics for recent social posts so they stay current beside the click
/// data. Runs one pass at startup then every <see cref="SocialMetricsOptions.RefreshIntervalMinutes"/>,
/// mirroring the domain re-verification service's pattern.
/// </summary>
public sealed class SocialMetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<SocialMetricsOptions> options,
    ILogger<SocialMetricsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.RefreshIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ISocialMetricsService>();
                var updated = await svc.RefreshRecentAsync(stoppingToken);
                if (updated > 0)
                    logger.LogInformation("Social metrics refreshed for {Count} post(s).", updated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Social metrics refresh pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
