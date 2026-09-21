using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IHardwareInventoryApiClient
{
    Task SendAsync(HardwareInventory inventory, CancellationToken cancellationToken);
}
