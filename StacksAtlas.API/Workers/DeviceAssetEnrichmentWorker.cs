using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StacksAtlas.Core.Database;
using StacksAtlas.Core.Services.Devices;
using StacksAtlas.Core.Services.Integrations;
using StacksAtlas.Core.State;

namespace StacksAtlas.API.Workers;

/// <summary>
/// Periodically enriches linked OpenAVC devices with serial/firmware from driver state.
/// </summary>
public class DeviceAssetEnrichmentWorker(
    IServiceProvider serviceProvider,
    ILogger<DeviceAssetEnrichmentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ExecutionState.IsHubBrainEnabled || ExecutionState.IsPortable)
            return;

        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<OpenAvcSettingsRepository>().GetSettings();
                if (!settings.Enabled)
                {
                    await Task.Delay(Interval, stoppingToken);
                    continue;
                }

                var linkRepo = scope.ServiceProvider.GetRequiredService<OpenAvcDeviceLinkRepository>();
                var enrichment = scope.ServiceProvider.GetRequiredService<DeviceAssetEnrichmentService>();
                foreach (var link in linkRepo.GetAll())
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await enrichment.EnrichLinkedDeviceAsync(
                            link.StacksAtlasDeviceId,
                            link.StacksAtlasMacAddress,
                            link.StacksAtlasIp,
                            cancellationToken: stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(
                            ex,
                            "Asset enrichment skipped for linked device {DeviceId}",
                            link.StacksAtlasDeviceId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Device asset enrichment worker cycle failed");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
