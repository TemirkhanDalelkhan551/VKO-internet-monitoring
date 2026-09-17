using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface ISystemLoadGuard
{
    Task<SystemLoadDecision> WaitUntilAvailableAsync(CancellationToken cancellationToken);
}
