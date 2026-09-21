using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public interface IDeviceInventoryRepository
{
    Task<DeviceInventoryRecord?> GetAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DeviceInventorySaveResult> SaveAsync(HardwareInventory inventory, CancellationToken cancellationToken);
}

public sealed record DeviceInventoryRecord(HardwareInventory Inventory, DateTimeOffset UpdatedAtUtc, DateTimeOffset? ChangedAtUtc);
public sealed record DeviceInventorySaveResult(bool Changed, DateTimeOffset UpdatedAtUtc);
