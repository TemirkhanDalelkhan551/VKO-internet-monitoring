using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class HardwareInventoryDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_DeletesSnapshotOnlyAfterSuccessfulDelivery()
    {
        var inventory = CreateInventory();
        var outbox = new InMemoryOutbox(inventory);
        var dispatcher = new HardwareInventoryDispatcher(outbox, new SuccessfulClient());

        Assert.True(await dispatcher.DispatchAsync(CancellationToken.None));
        Assert.Null(await outbox.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DispatchAsync_KeepsSnapshotWhenDeliveryFails()
    {
        var inventory = CreateInventory();
        var outbox = new InMemoryOutbox(inventory);
        var dispatcher = new HardwareInventoryDispatcher(outbox, new FailingClient());

        await Assert.ThrowsAsync<HttpRequestException>(() => dispatcher.DispatchAsync(CancellationToken.None));
        Assert.Equal(inventory, await outbox.GetPendingAsync(CancellationToken.None));
    }

    private static HardwareInventory CreateInventory() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow,
        new ComputerInventory(null, null, null, null, null), new OperatingSystemInventory(null, null, null, null, null, null), [],
        new MemoryInventory(null, null, []), [], [], new FirmwareInventory(null, null, null, null, null), []);

    private sealed class InMemoryOutbox(HardwareInventory? item) : IHardwareInventoryOutbox
    {
        private HardwareInventory? _item = item;
        public Task StoreAsync(HardwareInventory inventory, CancellationToken cancellationToken) { _item = inventory; return Task.CompletedTask; }
        public Task<HardwareInventory?> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult(_item);
        public Task MarkAsSentAsync(CancellationToken cancellationToken) { _item = null; return Task.CompletedTask; }
    }

    private sealed class SuccessfulClient : IHardwareInventoryApiClient { public Task SendAsync(HardwareInventory inventory, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class FailingClient : IHardwareInventoryApiClient { public Task SendAsync(HardwareInventory inventory, CancellationToken cancellationToken) => throw new HttpRequestException("offline"); }
}
