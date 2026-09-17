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
    MeasurementSnapshot? LatestMeasurement);

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
    MeasurementSnapshot? LatestMeasurement);

public sealed record LocalDeviceStatus(
    DeviceOverview Device,
    IReadOnlyList<VkoMonitoring.Agent.Core.Domain.InternetMeasurement> RecentMeasurements);

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
