using System.Text.Json.Serialization;

namespace VkoMonitoring.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<MonitoringStatus>))]
public enum MonitoringStatus
{
    Unknown,
    Normal,
    Unstable,
    Critical,
    NoConnection
}

[JsonConverter(typeof(JsonStringEnumConverter<MeasurementFreshness>))]
public enum MeasurementFreshness { Missing, Fresh, Stale }

[JsonConverter(typeof(JsonStringEnumConverter<AgentPresence>))]
public enum AgentPresence { NotSeen, Active, Inactive, Blocked }

public sealed record MeasurementSnapshot(
    double? DownloadMbps,
    double? UploadMbps,
    double? PingMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent,
    DateTimeOffset MeasuredAtUtc);

public sealed record SchoolOverview(
    Guid SchoolId,
    string Name,
    string? DistrictCity,
    string? Address,
    string? ProviderName,
    string? ConnectionType,
    double? ContractedDownloadMbps,
    double? ContractedUploadMbps,
    int DeviceCount,
    int ActiveDeviceCount,
    MonitoringStatus Status,
    MeasurementSnapshot? LatestMeasurement)
{
    public Guid? PrimaryLineId { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? ResponsibleName { get; init; }
    public string? ResponsiblePosition { get; init; }
    public string? ResponsiblePhone { get; init; }
    public string? ResponsibleEmail { get; init; }
    public MonitoringStatus QualityStatus { get; init; }
    public MeasurementFreshness MeasurementFreshness { get; init; }
    public AgentPresence AgentPresence { get; init; }
    public IReadOnlyList<LineOverview> Lines { get; init; } = [];
}

public sealed record DeviceOverview(
    Guid DeviceId,
    Guid LineId,
    string DeviceIdentifier,
    string Name,
    string? Room,
    string? ConnectionType,
    DateTimeOffset? LastSeenAtUtc,
    string? AgentVersion,
    bool IsBlocked,
    MonitoringStatus Status,
    MeasurementSnapshot? LatestMeasurement)
{
    public MonitoringStatus QualityStatus { get; init; }
    public MeasurementFreshness MeasurementFreshness { get; init; }
    public AgentPresence AgentPresence { get; init; }
    public string LifecycleStatus { get; init; } = "Active";
    public Guid? ReplacedByDeviceId { get; init; }
    public DateTimeOffset? RetiredAtUtc { get; init; }
    public string? RetirementReason { get; init; }
}

public sealed record LineOverview(
    Guid SchoolId,
    Guid LineId,
    string Name,
    string LineStatus,
    string? ProviderName,
    string? ConnectionType,
    double? ContractedDownloadMbps,
    double? ContractedUploadMbps,
    int DeviceCount,
    int ActiveDeviceCount,
    DateTimeOffset? LastSeenAtUtc,
    MonitoringStatus Status,
    MeasurementSnapshot? LatestMeasurement)
{
    public string? ContractNumber { get; init; }
    public DateOnly? ContractDate { get; init; }
    public MonitoringStatus QualityStatus { get; init; }
    public MeasurementFreshness MeasurementFreshness { get; init; }
    public AgentPresence AgentPresence { get; init; }
}

public sealed record LocalDeviceStatus(
    DeviceOverview Device,
    IReadOnlyList<VkoMonitoring.Agent.Core.Domain.InternetMeasurement> RecentMeasurements)
{
    public LocalQualityThresholds? QualityThresholds { get; init; }
}

public sealed record LocalQualityThresholds(
    decimal MinimumDownloadMbps,
    decimal MinimumUploadMbps,
    decimal MaximumPingMilliseconds,
    decimal MaximumJitterMilliseconds,
    decimal MaximumPacketLossPercent);

public sealed record AnalyticsOverview(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MeasurementCount,
    int ProblemMeasurementCount,
    double ProblemMeasurementPercent,
    double AvailabilityPercent,
    double? AverageDownloadMbps,
    double? MinimumDownloadMbps,
    double? MaximumDownloadMbps,
    double? AverageUploadMbps,
    double? MinimumUploadMbps,
    double? MaximumUploadMbps,
    double? AveragePingMilliseconds,
    double? MinimumPingMilliseconds,
    double? MaximumPingMilliseconds);
