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
    DateTimeOffset? RecoveredAtUtc,
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
