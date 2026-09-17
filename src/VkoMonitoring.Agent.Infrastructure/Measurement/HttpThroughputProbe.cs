using System.Diagnostics;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class HttpThroughputProbe(
    HttpClient httpClient,
    AgentOptions options) : IThroughputProbe
{
    public async Task<double> MeasureDownloadAsync(CancellationToken cancellationToken)
    {
        var uri = AddCacheBuster(options.DownloadTestUrl);
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[64 * 1024];
        var totalBytes = 0L;
        var stopwatch = Stopwatch.StartNew();

        while (totalBytes < options.DownloadBytes)
        {
            var requested = (int)Math.Min(buffer.Length, options.DownloadBytes - totalBytes);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
        }

        stopwatch.Stop();
        if (totalBytes == 0)
        {
            throw new HttpRequestException("The download test returned no data.");
        }

        return CalculateMbps(totalBytes, stopwatch.Elapsed);
    }

    public async Task<double> MeasureUploadAsync(CancellationToken cancellationToken)
    {
        var contentBytes = GC.AllocateUninitializedArray<byte>(options.UploadBytes);
        Random.Shared.NextBytes(contentBytes);
        using var content = new ByteArrayContent(contentBytes);
        content.Headers.ContentType = new("application/octet-stream");

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.PostAsync(options.UploadTestUrl, content, cancellationToken);
        stopwatch.Stop();
        response.EnsureSuccessStatusCode();

        return CalculateMbps(contentBytes.Length, stopwatch.Elapsed);
    }

    private static Uri AddCacheBuster(string url)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new Uri($"{url}{separator}cache={Guid.NewGuid():N}");
    }

    private static double CalculateMbps(long bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        return Math.Round(bytes * 8d / seconds / 1_000_000d, 2);
    }
}
