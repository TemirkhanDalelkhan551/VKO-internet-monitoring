using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IHeartbeatApiClient
{
    Task SendAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken);
}
