using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public interface IDevicePresenceRepository
{
    Task<bool> RecordHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken);
}
