using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Infrastructure.Outbox;

namespace VkoMonitoring.Agent.Tests;

public sealed class JsonFileHardwareInventoryOutboxTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"vko-inventory-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task StoreAsync_ReplacesOlderSnapshotUntilCurrentSnapshotIsAcknowledged()
    {
        var outbox = new JsonFileHardwareInventoryOutbox(new AgentOptions { DataDirectory = _directory });
        var first = CreateInventory("first");
        var latest = CreateInventory("latest");

        await outbox.StoreAsync(first, CancellationToken.None);
        await outbox.StoreAsync(latest, CancellationToken.None);

        var pending = await outbox.GetPendingAsync(CancellationToken.None);
        Assert.NotNull(pending);
        Assert.Equal(latest.DeviceId, pending.DeviceId);
        Assert.Equal("latest", pending.Computer.Model);
        await outbox.MarkAsSentAsync(CancellationToken.None);
        Assert.Null(await outbox.GetPendingAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static HardwareInventory CreateInventory(string model) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow,
        new ComputerInventory("PC", "VKO", model, null, null),
        new OperatingSystemInventory("Windows", null, null, null, null, null), [],
        new MemoryInventory(null, null, []), [], [], new FirmwareInventory(null, null, null, null, null), []);
}
