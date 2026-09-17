using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public interface IMeasurementRepository
{
    Task<bool> AddIfNotExistsAsync(InternetMeasurement measurement, CancellationToken cancellationToken);
    Task<IReadOnlyList<InternetMeasurement>> GetRecentAsync(int limit, CancellationToken cancellationToken);
}
