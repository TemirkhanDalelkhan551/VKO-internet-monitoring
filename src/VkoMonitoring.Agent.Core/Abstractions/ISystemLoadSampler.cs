using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface ISystemLoadSampler
{
    Task<SystemLoadSnapshot> SampleAsync(CancellationToken cancellationToken);
}
