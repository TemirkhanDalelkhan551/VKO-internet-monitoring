using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent;

/// <summary>Performs a single, non-blocking equipment inventory after the agent starts.</summary>
public sealed class HardwareInventoryWorker(
    IHardwareInventoryCollector collector,
    IHardwareInventoryOutbox outbox,
    HardwareInventoryDispatcher dispatcher,
    DispatchRetryPolicy retryPolicy,
    TimeProvider timeProvider,
    ILogger<HardwareInventoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            var inventory = collector.Collect();
            await outbox.StoreAsync(inventory, stoppingToken);
            logger.LogInformation("Hardware inventory for device {DeviceId} was stored for delivery.", inventory.DeviceId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Hardware inventory collection or local storage failed.");
        }

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await dispatcher.DispatchAsync(stoppingToken))
                {
                    consecutiveFailures = 0;
                    logger.LogInformation("Hardware inventory was accepted by the monitoring API.");
                }
                else consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                consecutiveFailures++;
                logger.LogWarning(exception, "Hardware inventory remains in the local queue; retry is scheduled.");
            }
            await Task.Delay(retryPolicy.GetDelay(consecutiveFailures), timeProvider, stoppingToken);
        }
    }
}
