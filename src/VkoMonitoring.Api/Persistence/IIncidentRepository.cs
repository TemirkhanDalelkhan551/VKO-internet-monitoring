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

    Task<ManualIncidentCreationResult> CreateManualIncidentAsync(
        ManualIncidentCreateRequest request,
        CancellationToken cancellationToken);

    Task<IncidentStatusChangeOutcome> ChangeStatusAsync(
        Guid incidentId,
        IncidentStatusChangeRequest request,
        CancellationToken cancellationToken);

    Task<bool> AssignAsync(
        Guid incidentId,
        IncidentAssignmentRequest request,
        CancellationToken cancellationToken);

    Task<bool> AddCommentAsync(
        Guid incidentId,
        IncidentCommentCreateRequest request,
        CancellationToken cancellationToken);
}
