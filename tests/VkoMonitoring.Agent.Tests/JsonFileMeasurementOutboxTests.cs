using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Infrastructure.Outbox;

namespace VkoMonitoring.Agent.Tests;

public sealed class JsonFileMeasurementOutboxTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"vko-agent-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task EnqueueAndMarkAsSent_PersistsUntilAcknowledged()
    {
        var outbox = new JsonFileMeasurementOutbox(new AgentOptions { DataDirectory = _directory });
        var measurement = CreateMeasurement();

        await outbox.EnqueueAsync(measurement, CancellationToken.None);
        var pending = await outbox.GetPendingAsync(CancellationToken.None);

        Assert.Single(pending);
        Assert.Equal(measurement, pending[0]);

        await outbox.MarkAsSentAsync(measurement.EventId, CancellationToken.None);
        Assert.Empty(await outbox.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetPendingAsync_WhenFileIsCorrupted_QuarantinesItAndContinues()
    {
        var outbox = new JsonFileMeasurementOutbox(new AgentOptions { DataDirectory = _directory });
        var measurement = CreateMeasurement();
        await outbox.EnqueueAsync(measurement, CancellationToken.None);
        var corruptedPath = Path.Combine(_directory, "outbox", "corrupted.json");
        await File.WriteAllTextAsync(corruptedPath, "not-json");

        var pending = await outbox.GetPendingAsync(CancellationToken.None);

        Assert.Single(pending);
        Assert.Equal(measurement.EventId, pending[0].EventId);
        Assert.False(File.Exists(corruptedPath));
        Assert.True(File.Exists(Path.Combine(_directory, "quarantine", "corrupted.json")));
    }

    [Fact]
    public async Task EnqueueAsync_WhenCapacityIsReached_DoesNotDeleteExistingMeasurement()
    {
        var outbox = new JsonFileMeasurementOutbox(new AgentOptions
        {
            DataDirectory = _directory,
            MaxQueuedMeasurements = 1
        });
        var firstMeasurement = CreateMeasurement();
        await outbox.EnqueueAsync(firstMeasurement, CancellationToken.None);

        await Assert.ThrowsAsync<OutboxCapacityExceededException>(() =>
            outbox.EnqueueAsync(CreateMeasurement(), CancellationToken.None));

        var pending = await outbox.GetPendingAsync(CancellationToken.None);
        Assert.Single(pending);
        Assert.Equal(firstMeasurement.EventId, pending[0].EventId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static InternetMeasurement CreateMeasurement() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        100,
        50,
        20,
        5,
        0,
        ConnectionStatus.Online,
        null,
        "1.0.0");
}
