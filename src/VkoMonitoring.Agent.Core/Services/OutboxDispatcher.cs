using VkoMonitoring.Agent.Core.Abstractions;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class OutboxDispatcher(
    IMeasurementOutbox outbox,
    IMeasurementApiClient apiClient)
{
    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        var sentCount = 0;
        var pending = await outbox.GetPendingAsync(cancellationToken);

        foreach (var measurement in pending)
        {
            await apiClient.SendAsync(measurement, cancellationToken);
            await outbox.MarkAsSentAsync(measurement.EventId, cancellationToken);
            sentCount++;
        }

        return sentCount;
    }
}
