using VkoMonitoring.Agent.Core.Abstractions;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class HardwareInventoryDispatcher(
    IHardwareInventoryOutbox outbox,
    IHardwareInventoryApiClient apiClient)
{
    public async Task<bool> DispatchAsync(CancellationToken cancellationToken)
    {
        var inventory = await outbox.GetPendingAsync(cancellationToken);
        if (inventory is null) return false;
        await apiClient.SendAsync(inventory, cancellationToken);
        await outbox.MarkAsSentAsync(cancellationToken);
        return true;
    }
}
