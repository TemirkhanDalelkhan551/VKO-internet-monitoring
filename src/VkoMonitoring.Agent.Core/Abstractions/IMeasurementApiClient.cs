using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IMeasurementApiClient
{
    Task<MeasurementDeliveryResult> SendAsync(
        InternetMeasurement measurement,
        CancellationToken cancellationToken);
}
