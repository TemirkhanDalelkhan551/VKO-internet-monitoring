using System.Text.Json.Serialization;

namespace VkoMonitoring.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<IncidentStatus>))]
public enum IncidentStatus
{
    New,
    SentToProvider,
    InProgress,
    WaitingForInformation,
    Resolved,
    Closed
}

[JsonConverter(typeof(JsonStringEnumConverter<IncidentSource>))]
public enum IncidentSource
{
    Automatic,
    Manual
}

public enum IncidentNotificationKind { Opened, Recovered }

public sealed record IncidentNotificationEvent(Guid IncidentId, IncidentNotificationKind Kind);

public sealed record IncidentOverview(
    Guid IncidentId,
    string IncidentNumber,
    Guid SchoolId,
    string SchoolName,
    Guid LineId,
    string LineName,
    string? ProviderName,
    IncidentSource Source,
    string ProblemType,
    IncidentStatus Status,
    string Title,
    string Description,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? SentToProviderAtUtc,
    DateTimeOffset? RecoveredAtUtc,
    DateTimeOffset? ClosedAtUtc,
    long DurationSeconds,
    Guid? LatestMeasurementEventId,
    string? AssignedTo);

public sealed record IncidentHistoryEntry(
    long EntryId,
    DateTimeOffset OccurredAtUtc,
    string Action,
    IncidentStatus? PreviousStatus,
    IncidentStatus? NewStatus,
    string? Comment,
    string? Actor);

public sealed record IncidentDetails(
    IncidentOverview Incident,
    IReadOnlyList<IncidentHistoryEntry> History);

public sealed record ManualIncidentCreateRequest(
    Guid SchoolId,
    Guid LineId,
    string ProblemType,
    string Title,
    string Description,
    DateTimeOffset? StartedAtUtc,
    string? AssignedTo,
    string Actor,
    string? Comment);

public sealed record ManualIncidentCreateResult(Guid IncidentId);

// Sent by an installed monitoring agent. The server obtains measurement facts itself,
// so the client cannot fabricate the indicators attached to an appeal.
public sealed record DeviceProblemReportRequest(
    Guid SchoolId,
    Guid LineId,
    string Comment);

public sealed record DeviceProblemReportResult(
    Guid IncidentId,
    bool Created);

public sealed record IncidentStatusChangeRequest(
    IncidentStatus Status,
    string Actor,
    string? Comment);

public sealed record IncidentAssignmentRequest(
    string? AssignedTo,
    string Actor,
    string? Comment);

public sealed record IncidentCommentCreateRequest(
    string Comment,
    string Actor);

public enum ManualIncidentCreationOutcome
{
    Created,
    BindingNotFound,
    OpenIncidentExists
}

public sealed record ManualIncidentCreationResult(
    ManualIncidentCreationOutcome Outcome,
    Guid? IncidentId);

public enum IncidentStatusChangeOutcome
{
    Updated,
    NotFound,
    InvalidTransition
}
