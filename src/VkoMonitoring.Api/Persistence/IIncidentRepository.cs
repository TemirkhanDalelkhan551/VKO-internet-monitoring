using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public interface IIncidentRepository
{
    Task ProcessLatestMeasurementAsync(Guid lineId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentOverview>> GetIncidentsAsync(
        Guid? schoolId,
        IncidentStatus? status,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken);

    Task<IncidentDetails?> GetIncidentAsync(Guid incidentId, CancellationToken cancellationToken);
}
