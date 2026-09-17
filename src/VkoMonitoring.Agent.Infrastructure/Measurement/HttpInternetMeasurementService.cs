using System.Diagnostics;
using System.Net.NetworkInformation;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class HttpInternetMeasurementService(
    ILatencyProbe latencyProbe,
    IThroughputProbe throughputProbe,
    INetworkContextProvider networkContextProvider,
    AgentOptions options,
    TimeProvider timeProvider) : IInternetMeasurementService
{
    public async Task<InternetMeasurement> MeasureAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var measuredAtUtc = timeProvider.GetUtcNow();
        var connectionType = networkContextProvider.GetConnectionType();
        var latency = await RunProbeAsync(
            "latency",
            () => latencyProbe.MeasureAsync(cancellationToken),
            cancellationToken);
        var download = await RunProbeAsync(
            "download",
            () => throughputProbe.MeasureDownloadAsync(cancellationToken),
            cancellationToken);
        var upload = await RunProbeAsync(
            "upload",
            () => throughputProbe.MeasureUploadAsync(cancellationToken),
            cancellationToken);

        var successfulProbeCount = CountSuccessfulProbes(latency, download, upload);
        var status = successfulProbeCount switch
        {
            3 => ConnectionStatus.Online,
            0 => ConnectionStatus.Offline,
            _ => ConnectionStatus.Degraded
        };
        var failureKind = DetermineFailureKind(latency, download, upload);
        var failureReason = BuildFailureReason(latency, download, upload);
        stopwatch.Stop();

        return new InternetMeasurement(
            Guid.NewGuid(),
            options.SchoolId,
            options.DeviceId,
            options.LineId,
            measuredAtUtc,
            download.Succeeded ? download.Value : null,
            upload.Succeeded ? upload.Value : null,
            latency.Succeeded ? latency.Value.AverageMilliseconds : null,
            latency.Succeeded ? latency.Value.JitterMilliseconds : null,
            latency.Succeeded ? latency.Value.PacketLossPercent : null,
            status,
            failureReason,
            typeof(HttpInternetMeasurementService).Assembly.GetName().Version?.ToString() ?? "unknown")
        {
            FailureKind = failureKind,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            NetworkConnectionType = connectionType,
            MeasurementServer = GetMeasurementServer(options.DownloadTestUrl)
        };
    }

    private static string GetMeasurementServer(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private static int CountSuccessfulProbes(
        ProbeResult<LatencyMetrics> latency,
        ProbeResult<double> download,
        ProbeResult<double> upload) =>
        (latency.Succeeded ? 1 : 0) +
        (download.Succeeded ? 1 : 0) +
        (upload.Succeeded ? 1 : 0);

    private static MeasurementFailureKind DetermineFailureKind(
        ProbeResult<LatencyMetrics> latency,
        ProbeResult<double> download,
        ProbeResult<double> upload)
    {
        if (latency.Succeeded && download.Succeeded && upload.Succeeded)
        {
            return MeasurementFailureKind.None;
        }

        if (!latency.Succeeded && !download.Succeeded && !upload.Succeeded)
        {
            return MeasurementFailureKind.InternetUnavailable;
        }

        if (latency.Succeeded && !download.Succeeded && !upload.Succeeded)
        {
            return MeasurementFailureKind.MeasurementServerUnavailable;
        }

        return MeasurementFailureKind.PartialMeasurement;
    }

    private static string? BuildFailureReason(
        ProbeResult<LatencyMetrics> latency,
        ProbeResult<double> download,
        ProbeResult<double> upload)
    {
        var errors = new[] { latency.Error, download.Error, upload.Error }
            .Where(error => error is not null);
        var result = string.Join(" | ", errors);
        return result.Length == 0 ? null : result;
    }

    private static async Task<ProbeResult<T>> RunProbeAsync<T>(
        string probeName,
        Func<Task<T>> probe,
        CancellationToken cancellationToken)
    {
        try
        {
            return ProbeResult<T>.Success(await probe());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedNetworkFailure(exception))
        {
            return ProbeResult<T>.Failure($"{probeName}: {exception.Message}");
        }
    }

    private static bool IsExpectedNetworkFailure(Exception exception) =>
        exception is HttpRequestException or PingException or OperationCanceledException;

    private sealed record ProbeResult<T>(T Value, string? Error)
    {
        public bool Succeeded => Error is null;

        public static ProbeResult<T> Success(T value) => new(value, null);

        public static ProbeResult<T> Failure(string error) => new(default!, error);
    }
}
