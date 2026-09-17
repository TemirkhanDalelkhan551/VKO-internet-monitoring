using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IInternetMeasurementService
{
    Task<InternetMeasurement> MeasureAsync(CancellationToken cancellationToken);
}
