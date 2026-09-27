using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent;

public sealed class OutboxWorker(
    OutboxDispatcher dispatcher,
    DispatchRetryPolicy retryPolicy,
    TimeProvider timeProvider,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatchSucceeded = await DispatchSafelyAsync(stoppingToken);
            consecutiveFailures = dispatchSucceeded ? 0 : consecutiveFailures + 1;

            var delay = retryPolicy.GetDelay(consecutiveFailures);
            if (!dispatchSucceeded)
            {
                logger.LogInformation(
                    "Next queued measurement delivery attempt is scheduled in {Delay}.",
                    delay);
            }

            await Task.Delay(delay, timeProvider, stoppingToken);
        }
    }

    private async Task<bool> DispatchSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await dispatcher.DispatchAsync(cancellationToken);
            if (result.SentCount > 0)
            {
                logger.LogInformation("Successfully sent {Count} queued measurements.", result.SentCount);
            }

            foreach (var rejected in result.QuarantinedMeasurements)
            {
                logger.LogError(
                    "Measurement {EventId} was permanently rejected by the API with HTTP {StatusCode} and moved to quarantine.",
                    rejected.EventId,
                    rejected.ResponseStatusCode);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Monitoring API is unavailable. Measurements remain in the local queue.");
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to dispatch queued measurements.");
            return false;
        }
    }
}
