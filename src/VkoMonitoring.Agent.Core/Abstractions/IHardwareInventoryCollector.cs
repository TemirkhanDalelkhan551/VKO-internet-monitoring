using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IHardwareInventoryCollector
{
    HardwareInventory Collect();
}
