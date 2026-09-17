using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedIncidentRepository : IIncidentRepository
{
    public Task ProcessLatestMeasurementAsync(Guid lineId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<IncidentOverview>> GetIncidentsAsync(
        Guid? schoolId,
        IncidentStatus? status,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<IncidentDetails?> GetIncidentAsync(
        Guid incidentId,
        CancellationToken cancellationToken) =>
        throw CreateException();

    private static NotSupportedException CreateException() =>
        new("Incidents require PostgreSQL storage.");
}
