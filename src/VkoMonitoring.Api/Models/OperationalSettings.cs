namespace VkoMonitoring.Api.Models;

public sealed record OperationalSettings(
    string[] MeasurementWindows,
    decimal MinimumDownloadMbps,
    decimal MinimumUploadMbps,
    decimal MaximumPingMilliseconds,
    decimal MaximumJitterMilliseconds,
    decimal MaximumPacketLossPercent,
    decimal MinimumAvailabilityPercent,
    DateTimeOffset? UpdatedAtUtc = null);
