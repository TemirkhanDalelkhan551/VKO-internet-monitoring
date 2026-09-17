namespace VkoMonitoring.Agent.Setup.Status;

public sealed record LocalDeviceStatusResponse(
    LocalDeviceOverview Device,
    IReadOnlyList<LocalMeasurement> RecentMeasurements);

public sealed record LocalDeviceOverview(
    Guid DeviceId,
    Guid LineId,
    string DeviceIdentifier,
    string Name,
    string? Room,
    string? ConnectionType,
    DateTimeOffset? LastSeenAtUtc,
    string? AgentVersion,
    bool IsBlocked,
    string Status,
    LocalMeasurementSnapshot? LatestMeasurement);

public sealed record LocalMeasurementSnapshot(
    double? DownloadMbps,
    double? UploadMbps,
    double? PingMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent,
    DateTimeOffset MeasuredAtUtc);

public sealed record LocalMeasurement(
    Guid EventId,
    DateTimeOffset MeasuredAtUtc,
    double? DownloadMbps,
    double? UploadMbps,
    double? PingMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent,
    string ConnectionStatus,
    string? FailureReason,
    string AgentVersion,
    long DurationMilliseconds,
    string? ExternalIpAddress,
    string NetworkConnectionType,
    string? MeasurementServer);
