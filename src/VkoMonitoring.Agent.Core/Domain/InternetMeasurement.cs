namespace VkoMonitoring.Agent.Core.Domain;

public sealed record InternetMeasurement(
    Guid EventId,
    Guid SchoolId,
    Guid DeviceId,
    Guid LineId,
    DateTimeOffset MeasuredAtUtc,
    double? DownloadMbps,
    double? UploadMbps,
    double? PingMilliseconds,
    double? JitterMilliseconds,
    double? PacketLossPercent,
    ConnectionStatus ConnectionStatus,
    string? FailureReason,
    string AgentVersion)
{
    public MeasurementFailureKind FailureKind { get; init; } = MeasurementFailureKind.None;
    public long DurationMilliseconds { get; init; }
    public string? ExternalIpAddress { get; init; }
    public NetworkConnectionType NetworkConnectionType { get; init; } = NetworkConnectionType.Unknown;
    public string? MeasurementServer { get; init; }
}
