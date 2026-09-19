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

        await service.RequestAsync(CancellationToken.None);

        var requestPath = Path.Combine(directory, MeasurementTriggerFile.Name);
        Assert.True(File.Exists(requestPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task RequestAsync_WhenServiceIsStopped_RejectsRequest()
    {
        var service = CreateService(() => false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestAsync(CancellationToken.None));

        Assert.Contains("не запущена", exception.Message, StringComparison.Ordinal);
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

    private LocalMeasurementRequestService CreateService(Func<bool> isRunning) => new(
        new SetupPaths(Path.Combine(directory, "appsettings.json"), directory),
        isRunning);
}
