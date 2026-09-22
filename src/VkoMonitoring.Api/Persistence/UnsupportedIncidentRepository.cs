using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedIncidentRepository : IIncidentRepository
{
    public Task<IncidentNotificationEvent?> ProcessLatestMeasurementAsync(Guid lineId, CancellationToken cancellationToken) =>
        Task.FromResult<IncidentNotificationEvent?>(null);

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

    public Task<Guid?> GetOpenIncidentIdAsync(Guid lineId, CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<ManualIncidentCreationResult> CreateManualIncidentAsync(
        ManualIncidentCreateRequest request,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<IncidentStatusChangeOutcome> ChangeStatusAsync(
        Guid incidentId,
        IncidentStatusChangeRequest request,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<bool> AssignAsync(
        Guid incidentId,
        IncidentAssignmentRequest request,
        CancellationToken cancellationToken) =>
        throw CreateException();

    public Task<bool> AddCommentAsync(
        Guid incidentId,
        IncidentCommentCreateRequest request,
        CancellationToken cancellationToken) =>
        throw CreateException();

    private static NotSupportedException CreateException() =>
        new("Incidents require PostgreSQL storage.");
}
