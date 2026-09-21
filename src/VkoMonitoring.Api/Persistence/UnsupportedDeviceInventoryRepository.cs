using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public sealed class UnsupportedDeviceInventoryRepository : IDeviceInventoryRepository
{
    public Task<DeviceInventoryRecord?> GetAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult<DeviceInventoryRecord?>(null);
    public Task<DeviceInventorySaveResult> SaveAsync(HardwareInventory inventory, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Hardware inventory requires PostgreSQL storage.");
}
