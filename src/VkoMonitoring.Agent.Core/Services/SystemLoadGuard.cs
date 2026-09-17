using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class SystemLoadGuard(
    ISystemLoadSampler sampler,
    AgentOptions options,
    TimeProvider timeProvider) : ISystemLoadGuard
{
    public async Task<SystemLoadDecision> WaitUntilAvailableAsync(
        CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow().AddSeconds(options.MaximumLoadDeferralSeconds);
        while (true)
        {
            var snapshot = await sampler.SampleAsync(cancellationToken);
            if (IsAvailable(snapshot))
            {
                return new SystemLoadDecision(true, snapshot);
            }

            var remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return new SystemLoadDecision(false, snapshot);
            }

            var delay = TimeSpan.FromSeconds(options.SystemLoadRetrySeconds);
            await Task.Delay(delay < remaining ? delay : remaining, timeProvider, cancellationToken);
        }
    }

    private bool IsAvailable(SystemLoadSnapshot snapshot) =>
        snapshot.CpuUsagePercent <= options.MaximumCpuUsagePercent &&
        snapshot.NetworkMegabitsPerSecond <= options.MaximumBackgroundNetworkMbps;
}
