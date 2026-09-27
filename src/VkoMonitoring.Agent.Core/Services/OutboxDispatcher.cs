using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class OutboxDispatcher(
    IMeasurementOutbox outbox,
    IMeasurementApiClient apiClient)
{
    public async Task<OutboxDispatchResult> DispatchAsync(CancellationToken cancellationToken)
    {
        var sentCount = 0;
        var quarantinedMeasurements = new List<QuarantinedMeasurement>();
        var pending = await outbox.GetPendingAsync(cancellationToken);

        foreach (var measurement in pending)
        {
            var delivery = await apiClient.SendAsync(measurement, cancellationToken);
            if (delivery.Outcome == MeasurementDeliveryOutcome.PermanentlyRejected)
            {
                await outbox.QuarantineAsync(measurement.EventId, cancellationToken);
                quarantinedMeasurements.Add(new QuarantinedMeasurement(
                    measurement.EventId,
                    delivery.ResponseStatusCode));
                continue;
            }

            await outbox.MarkAsSentAsync(measurement.EventId, cancellationToken);
            sentCount++;
        }

        return new OutboxDispatchResult(sentCount, quarantinedMeasurements);
    }
}

public sealed record QuarantinedMeasurement(Guid EventId, int? ResponseStatusCode);

public sealed record OutboxDispatchResult(
    int SentCount,
    IReadOnlyList<QuarantinedMeasurement> QuarantinedMeasurements);
