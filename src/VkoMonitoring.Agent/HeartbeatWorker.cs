using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent;

public sealed class HeartbeatWorker(
    IHeartbeatApiClient apiClient,
    DailyMeasurementSchedule schedule,
    AgentOptions options,
    TimeProvider timeProvider,
    ILogger<HeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.HeartbeatIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendSafelyAsync(stoppingToken);
            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    private async Task SendSafelyAsync(CancellationToken cancellationToken)
    {
        var heartbeat = new AgentHeartbeat(
            options.SchoolId,
            options.DeviceId,
            options.LineId,
            timeProvider.GetUtcNow(),
            typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "unknown");

        try
        {
            var configuration = await apiClient.SendAsync(heartbeat, cancellationToken);
            if (configuration is not null && !schedule.TryUpdateWindows(configuration.MeasurementWindows))
                logger.LogWarning("Server returned invalid or empty measurement windows; existing schedule is preserved.");
            logger.LogDebug("Heartbeat for device {DeviceId} was accepted.", options.DeviceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Monitoring API is unavailable for heartbeat delivery.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Heartbeat delivery failed unexpectedly.");
        }
    }
}
