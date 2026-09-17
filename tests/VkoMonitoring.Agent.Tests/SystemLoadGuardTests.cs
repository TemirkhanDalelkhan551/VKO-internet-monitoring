using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class SystemLoadGuardTests
{
    [Fact]
    public async Task WaitUntilAvailableAsync_WhenLoadIsLow_AllowsMeasurement()
    {
        var guard = CreateGuard(new SystemLoadSnapshot(25, 1));

        var decision = await guard.WaitUntilAvailableAsync(CancellationToken.None);

        Assert.True(decision.CanRunMeasurement);
        Assert.Equal(25, decision.Snapshot.CpuUsagePercent);
    }

    [Theory]
    [InlineData(71, 1)]
    [InlineData(25, 6)]
    public async Task WaitUntilAvailableAsync_WhenLoadRemainsHigh_SkipsMeasurement(
        double cpuPercent,
        double networkMbps)
    {
        var guard = CreateGuard(new SystemLoadSnapshot(cpuPercent, networkMbps));

        var decision = await guard.WaitUntilAvailableAsync(CancellationToken.None);

        Assert.False(decision.CanRunMeasurement);
    }

    private static SystemLoadGuard CreateGuard(SystemLoadSnapshot snapshot) =>
        new(
            new FixedSampler(snapshot),
            new AgentOptions
            {
                MaximumCpuUsagePercent = 70,
                MaximumBackgroundNetworkMbps = 5,
                MaximumLoadDeferralSeconds = 0
            },
            TimeProvider.System);

    private sealed class FixedSampler(SystemLoadSnapshot snapshot) : ISystemLoadSampler
    {
        public Task<SystemLoadSnapshot> SampleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
