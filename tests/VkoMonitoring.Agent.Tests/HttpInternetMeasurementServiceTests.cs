using System.Net.NetworkInformation;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Infrastructure.Measurement;

namespace VkoMonitoring.Agent.Tests;

public sealed class HttpInternetMeasurementServiceTests
{
    [Fact]
    public async Task MeasureAsync_WhenAllProbesSucceed_ReturnsOnlineMeasurement()
    {
        var service = CreateService(
            new SuccessfulLatencyProbe(),
            new ConfigurableThroughputProbe(downloadMbps: 100, uploadMbps: 50));

        var result = await service.MeasureAsync(CancellationToken.None);

        Assert.Equal(ConnectionStatus.Online, result.ConnectionStatus);
        Assert.Equal(MeasurementFailureKind.None, result.FailureKind);
        Assert.Equal(100, result.DownloadMbps);
        Assert.Equal(50, result.UploadMbps);
        Assert.Equal(20, result.PingMilliseconds);
        Assert.Null(result.FailureReason);
        Assert.Equal(NetworkConnectionType.WiFi, result.NetworkConnectionType);
        Assert.Equal("speed.example.kz", result.MeasurementServer);
        Assert.True(result.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task MeasureAsync_WhenMeasurementServerFails_PreservesLatencyAndReturnsDegraded()
    {
        var service = CreateService(
            new SuccessfulLatencyProbe(),
            new ConfigurableThroughputProbe(new HttpRequestException("server unavailable")));

        var result = await service.MeasureAsync(CancellationToken.None);

        Assert.Equal(ConnectionStatus.Degraded, result.ConnectionStatus);
        Assert.Equal(MeasurementFailureKind.MeasurementServerUnavailable, result.FailureKind);
        Assert.Equal(20, result.PingMilliseconds);
        Assert.Null(result.DownloadMbps);
        Assert.Null(result.UploadMbps);
    }

    [Fact]
    public async Task MeasureAsync_WhenLatencyFails_PreservesThroughputAndReturnsDegraded()
    {
        var service = CreateService(
            new FailingLatencyProbe(),
            new ConfigurableThroughputProbe(downloadMbps: 100, uploadMbps: 50));

        var result = await service.MeasureAsync(CancellationToken.None);

        Assert.Equal(ConnectionStatus.Degraded, result.ConnectionStatus);
        Assert.Equal(MeasurementFailureKind.PartialMeasurement, result.FailureKind);
        Assert.Equal(100, result.DownloadMbps);
        Assert.Equal(50, result.UploadMbps);
        Assert.Null(result.PingMilliseconds);
    }

    [Fact]
    public async Task MeasureAsync_WhenAllProbesFail_ReturnsOfflineMeasurement()
    {
        var service = CreateService(
            new FailingLatencyProbe(),
            new ConfigurableThroughputProbe(new HttpRequestException("server unavailable")));

        var result = await service.MeasureAsync(CancellationToken.None);

        Assert.Equal(ConnectionStatus.Offline, result.ConnectionStatus);
        Assert.Equal(MeasurementFailureKind.InternetUnavailable, result.FailureKind);
        Assert.Null(result.DownloadMbps);
        Assert.Null(result.UploadMbps);
        Assert.Null(result.PingMilliseconds);
        Assert.Contains("latency:", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("download:", result.FailureReason, StringComparison.Ordinal);
        Assert.Contains("upload:", result.FailureReason, StringComparison.Ordinal);
    }

    private static HttpInternetMeasurementService CreateService(
        ILatencyProbe latencyProbe,
        IThroughputProbe throughputProbe) =>
        new(
            latencyProbe,
            throughputProbe,
            new FixedNetworkContextProvider(),
            new AgentOptions
            {
                SchoolId = Guid.NewGuid(),
                DeviceId = Guid.NewGuid(),
                LineId = Guid.NewGuid(),
                DownloadTestUrl = "https://speed.example.kz/download"
            },
            new FixedTimeProvider());

    private sealed class FixedNetworkContextProvider : INetworkContextProvider
    {
        public NetworkConnectionType GetConnectionType() => NetworkConnectionType.WiFi;
    }

    private sealed class SuccessfulLatencyProbe : ILatencyProbe
    {
        public Task<LatencyMetrics> MeasureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new LatencyMetrics(20, 3, 0));
    }

    private sealed class FailingLatencyProbe : ILatencyProbe
    {
        public Task<LatencyMetrics> MeasureAsync(CancellationToken cancellationToken) =>
            throw new PingException("ping unavailable");
    }

    private sealed class ConfigurableThroughputProbe : IThroughputProbe
    {
        private readonly double _downloadMbps;
        private readonly double _uploadMbps;
        private readonly Exception? _exception;

        public ConfigurableThroughputProbe(double downloadMbps, double uploadMbps)
        {
            _downloadMbps = downloadMbps;
            _uploadMbps = uploadMbps;
        }

        public ConfigurableThroughputProbe(Exception exception)
        {
            _exception = exception;
        }

        public Task<double> MeasureDownloadAsync(CancellationToken cancellationToken) =>
            _exception is null
                ? Task.FromResult(_downloadMbps)
                : Task.FromException<double>(_exception);

        public Task<double> MeasureUploadAsync(CancellationToken cancellationToken) =>
            _exception is null
                ? Task.FromResult(_uploadMbps)
                : Task.FromException<double>(_exception);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
    }
}
