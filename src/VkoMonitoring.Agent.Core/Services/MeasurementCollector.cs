using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class MeasurementCollector(
    IInternetMeasurementService measurementService,
    IMeasurementOutbox outbox)
{
    public async Task<InternetMeasurement> CollectAsync(CancellationToken cancellationToken)
    {
        var measurement = await measurementService.MeasureAsync(cancellationToken);
        await outbox.EnqueueAsync(measurement, cancellationToken);
        return measurement;
    }
}
