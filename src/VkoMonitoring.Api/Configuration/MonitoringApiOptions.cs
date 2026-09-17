namespace VkoMonitoring.Api.Configuration;

public sealed class MonitoringApiOptions
{
    public const string SectionName = "MonitoringApi";

    public string DataDirectory { get; init; } = "data";
    public string StorageProvider { get; init; } = "Json";
    public string AdminToken { get; init; } = string.Empty;
    public Dictionary<string, string> DeviceTokens { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DeviceBindingOptions> DeviceBindings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public QualityThresholdOptions Thresholds { get; init; } = new();
    public int DeviceActiveWindowMinutes { get; init; } = 15;
    public int MaximumSpeedTestBytes { get; init; } = 20_000_000;
    public int ReadinessDegradedAfterMilliseconds { get; init; } = 1_000;
}

public sealed class DeviceBindingOptions
{
    public Guid SchoolId { get; init; }
    public Guid LineId { get; init; }
    public string SchoolName { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string LineName { get; init; } = string.Empty;
}

public sealed class QualityThresholdOptions
{
    public decimal MinimumDownloadMbps { get; init; } = 20;
    public decimal MinimumUploadMbps { get; init; } = 20;
    public decimal MaximumPingMilliseconds { get; init; } = 100;
    public decimal MaximumJitterMilliseconds { get; init; } = 30;
    public decimal MaximumPacketLossPercent { get; init; } = 2;
    public decimal MinimumAvailabilityPercent { get; init; } = 99;
}
