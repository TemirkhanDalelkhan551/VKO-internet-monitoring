using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Infrastructure.Measurement;

namespace VkoMonitoring.Agent.Tests;

public sealed class FileMeasurementTriggerTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "vko-measurement-trigger-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WaitForRequestAsync_WhenRequestExists_ConsumesIt()
    {
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, MeasurementTriggerFile.Name);
        await File.WriteAllTextAsync(requestPath, DateTimeOffset.UtcNow.ToString("O"));
        var trigger = CreateTrigger();

        var requested = await trigger.WaitForRequestAsync(
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.True(requested);
        Assert.False(File.Exists(requestPath));
    }

    [Fact]
    public async Task WaitForRequestAsync_WhenNoRequestExists_TimesOut()
    {
        var trigger = CreateTrigger();

        var requested = await trigger.WaitForRequestAsync(
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        Assert.False(requested);
    }

    [Fact]
    public async Task WaitForRequestAsync_WhenCancelled_StopsWaiting()
    {
        var trigger = CreateTrigger();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            trigger.WaitForRequestAsync(TimeSpan.FromMinutes(1), cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FileMeasurementTrigger CreateTrigger() => new(
        new AgentOptions { DataDirectory = directory },
        TimeProvider.System);
}
