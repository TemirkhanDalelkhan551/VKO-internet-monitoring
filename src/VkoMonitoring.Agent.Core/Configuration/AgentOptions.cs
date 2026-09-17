namespace VkoMonitoring.Agent.Core.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public Guid SchoolId { get; init; }
    public Guid DeviceId { get; init; }
    public Guid LineId { get; init; }
    public string DeviceToken { get; init; } = string.Empty;
    public string DeviceTokenFile { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string DownloadTestUrl { get; init; } = string.Empty;
    public string UploadTestUrl { get; init; } = string.Empty;
    public string PingHost { get; init; } = string.Empty;
    public string DataDirectory { get; init; } = "data";
    public int DownloadBytes { get; init; } = 5_000_000;
    public int UploadBytes { get; init; } = 2_000_000;
    public int PingAttempts { get; init; } = 5;
    public int PingTimeoutMilliseconds { get; init; } = 2_000;
    public int DispatchIntervalSeconds { get; init; } = 60;
    public int MaxDispatchIntervalSeconds { get; init; } = 900;
    public int DispatchJitterSeconds { get; init; } = 15;
    public int HeartbeatIntervalSeconds { get; init; } = 300;
    public int MaxQueuedMeasurements { get; init; } = 5_000;
    public int DiagnosticLogMaxFileSizeBytes { get; init; } = 5_000_000;
    public int DiagnosticLogRetentionDays { get; init; } = 14;
    public double MaximumCpuUsagePercent { get; init; } = 70;
    public double MaximumBackgroundNetworkMbps { get; init; } = 5;
    public int SystemLoadSampleMilliseconds { get; init; } = 1_000;
    public int SystemLoadRetrySeconds { get; init; } = 30;
    public int MaximumLoadDeferralSeconds { get; init; } = 600;
    public bool RunImmediatelyOnStartup { get; init; }
    public string[] MeasurementWindows { get; set; } =
        ["08:00-09:00", "12:00-13:00", "16:00-17:00", "20:00-21:00"];
}
