using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

/// <summary>Stores only the latest inventory snapshot until the server acknowledges it.</summary>
public interface IHardwareInventoryOutbox
{
    Task StoreAsync(HardwareInventory inventory, CancellationToken cancellationToken);
    Task<HardwareInventory?> GetPendingAsync(CancellationToken cancellationToken);
    Task MarkAsSentAsync(CancellationToken cancellationToken);
}
