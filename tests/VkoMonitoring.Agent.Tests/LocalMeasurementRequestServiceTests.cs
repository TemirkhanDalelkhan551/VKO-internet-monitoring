using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Setup.Activation;
using VkoMonitoring.Agent.Setup.Status;

namespace VkoMonitoring.Agent.Tests;

public sealed class LocalMeasurementRequestServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "vko-local-measurement-request-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RequestAsync_WhenServiceIsRunning_WritesRequestAtomically()
    {
        var service = CreateService(() => true);

        var result = await service.RequestAsync(CancellationToken.None);

        Assert.False(result.ServiceWasStarted);
        var requestPath = Path.Combine(directory, MeasurementTriggerFile.Name);
        Assert.True(File.Exists(requestPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task RequestAsync_WhenServiceIsStopped_StartsItAndWritesRequest()
    {
        var isRunning = false;
        var service = CreateService(
            () => isRunning,
            () => isRunning = true);

        var result = await service.RequestAsync(CancellationToken.None);

        Assert.True(result.ServiceWasStarted);
        Assert.True(File.Exists(Path.Combine(directory, MeasurementTriggerFile.Name)));
    }

    [Fact]
    public async Task RequestAsync_WhenServiceCannotStart_ReportsRecoveryFailure()
    {
        var service = CreateService(
            () => false,
            () => throw new InvalidOperationException("start failed"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestAsync(CancellationToken.None));

        Assert.Contains("автоматически запустить", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private LocalMeasurementRequestService CreateService(
        Func<bool> isRunning,
        Action? start = null) => new(
        new SetupPaths(Path.Combine(directory, "appsettings.json"), directory),
        isRunning,
        start ?? (() => { }));
}
