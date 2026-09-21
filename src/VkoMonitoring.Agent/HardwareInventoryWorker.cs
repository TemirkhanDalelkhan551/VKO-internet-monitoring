using VkoMonitoring.Agent.Core.Abstractions;

namespace VkoMonitoring.Agent;

/// <summary>Performs a single, non-blocking equipment inventory after the agent starts.</summary>
public sealed class HardwareInventoryWorker(
    IHardwareInventoryCollector collector,
    IHardwareInventoryApiClient apiClient,
    ILogger<HardwareInventoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            var inventory = collector.Collect();
            await apiClient.SendAsync(inventory, stoppingToken);
            logger.LogInformation("Hardware inventory for device {DeviceId} was accepted.", inventory.DeviceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Monitoring API is unavailable for hardware inventory delivery.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Hardware inventory collection or delivery failed.");
        }
    }
}
