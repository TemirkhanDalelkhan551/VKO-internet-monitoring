using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent;

public sealed class MeasurementWorker(
    MeasurementCollector collector,
    ISystemLoadGuard systemLoadGuard,
    IMeasurementSchedule schedule,
    IMeasurementTrigger trigger,
    AgentOptions options,
    TimeProvider timeProvider,
    ILogger<MeasurementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RunImmediatelyOnStartup)
        {
            await CollectSafelyAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetLocalNow();
            var nextRun = schedule.GetNextRun(now);
            var delay = nextRun - now;
            logger.LogInformation("Next internet measurement is scheduled for {NextRun}.", nextRun);

            var manuallyRequested = await trigger.WaitForRequestAsync(delay, stoppingToken);
            if (manuallyRequested)
            {
                logger.LogInformation("An immediate internet measurement was requested locally.");
            }

            await CollectSafelyAsync(stoppingToken);
        }
    }

    private async Task CollectSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var loadDecision = await systemLoadGuard.WaitUntilAvailableAsync(cancellationToken);
            if (!loadDecision.CanRunMeasurement)
            {
                logger.LogWarning(
                    "Measurement skipped to avoid interfering with the user. CPU={CpuUsagePercent}%, network={NetworkMbps} Mbps.",
                    loadDecision.Snapshot.CpuUsagePercent,
                    loadDecision.Snapshot.NetworkMegabitsPerSecond);
                return;
            }

            logger.LogInformation(
                "System load permits measurement: CPU={CpuUsagePercent}%, network={NetworkMbps} Mbps.",
                loadDecision.Snapshot.CpuUsagePercent,
                loadDecision.Snapshot.NetworkMegabitsPerSecond);

            var result = await collector.CollectAsync(cancellationToken);
            logger.LogInformation(
                "Measurement {EventId} collected with status {Status}: download={DownloadMbps} Mbps, upload={UploadMbps} Mbps, ping={PingMs} ms, duration={DurationMs} ms, connection={ConnectionType}.",
                result.EventId,
                result.ConnectionStatus,
                result.DownloadMbps,
                result.UploadMbps,
                result.PingMilliseconds,
                result.DurationMilliseconds,
                result.NetworkConnectionType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Internet measurement failed unexpectedly.");
        }
    }
}
