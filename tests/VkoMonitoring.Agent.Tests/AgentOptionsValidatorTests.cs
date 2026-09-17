using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Tests;

public sealed class AgentOptionsValidatorTests
{
    [Fact]
    public void Validate_AllowsHttpForLocalDevelopment()
    {
        var options = CreateValidOptions().WithUrls(
            "http://localhost:5080/",
            "http://127.0.0.1:5080/speed/download",
            "http://localhost:5080/speed/upload");

        AgentOptionsValidator.Validate(options);
    }

    [Fact]
    public void Validate_RejectsHttpForRemoteServer()
    {
        var options = CreateValidOptions().WithUrls(
            "http://monitoring.example.com/",
            "https://monitoring.example.com/speed/download",
            "https://monitoring.example.com/speed/upload");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AgentOptionsValidator.Validate(options));

        Assert.Contains("ApiBaseUrl must use HTTPS", exception.Message, StringComparison.Ordinal);
    }

    private static AgentOptions CreateValidOptions() => new()
    {
        SchoolId = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        LineId = Guid.NewGuid(),
        DeviceTokenFile = @"C:\ProgramData\VkoInternetMonitoringAgent\device-token.dat",
        ApiBaseUrl = "https://monitoring.example.com/",
        DownloadTestUrl = "https://monitoring.example.com/speed/download",
        UploadTestUrl = "https://monitoring.example.com/speed/upload",
        PingHost = "1.1.1.1",
        MeasurementWindows = ["08:00-09:00", "12:00-13:00", "16:00-17:00"]
    };
}

file static class AgentOptionsTestExtensions
{
    public static AgentOptions WithUrls(
        this AgentOptions source,
        string apiBaseUrl,
        string downloadTestUrl,
        string uploadTestUrl) =>
        new()
        {
            SchoolId = source.SchoolId,
            DeviceId = source.DeviceId,
            LineId = source.LineId,
            DeviceToken = source.DeviceToken,
            DeviceTokenFile = source.DeviceTokenFile,
            ApiBaseUrl = apiBaseUrl,
            DownloadTestUrl = downloadTestUrl,
            UploadTestUrl = uploadTestUrl,
            PingHost = source.PingHost,
            DataDirectory = source.DataDirectory,
            DownloadBytes = source.DownloadBytes,
            UploadBytes = source.UploadBytes,
            PingAttempts = source.PingAttempts,
            PingTimeoutMilliseconds = source.PingTimeoutMilliseconds,
            DispatchIntervalSeconds = source.DispatchIntervalSeconds,
            MaxDispatchIntervalSeconds = source.MaxDispatchIntervalSeconds,
            DispatchJitterSeconds = source.DispatchJitterSeconds,
            MaxQueuedMeasurements = source.MaxQueuedMeasurements,
            RunImmediatelyOnStartup = source.RunImmediatelyOnStartup,
            MeasurementWindows = source.MeasurementWindows
        };
}
