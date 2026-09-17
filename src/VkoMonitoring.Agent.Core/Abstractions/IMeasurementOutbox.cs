using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IMeasurementOutbox
{
    Task EnqueueAsync(InternetMeasurement measurement, CancellationToken cancellationToken);
    Task<IReadOnlyList<InternetMeasurement>> GetPendingAsync(CancellationToken cancellationToken);
    Task MarkAsSentAsync(Guid eventId, CancellationToken cancellationToken);
}
