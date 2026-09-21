namespace VkoMonitoring.Api.Models;

public sealed record ReportFilter(Guid? SchoolId, Guid[] DeviceIds, DateTimeOffset FromUtc,
    DateTimeOffset ToUtc, string? ConnectionStatus);

public sealed record MeasurementReportRow(Guid SchoolId, string SchoolName, Guid DeviceId,
    string DeviceName, string? Room, DateTimeOffset MeasuredAtUtc, double? DownloadMbps,
    double? UploadMbps, double? PingMilliseconds, double? JitterMilliseconds,
    double? PacketLossPercent, string ConnectionStatus, bool IsProblem)
{
    // Optional properties retain compatibility with existing report consumers.
    public Guid? LineId { get; init; }
    public string? LineName { get; init; }
}
