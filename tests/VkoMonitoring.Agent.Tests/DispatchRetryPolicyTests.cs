using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class DispatchRetryPolicyTests
{
    [Fact]
    public void GetDelay_WithoutFailures_ReturnsBaseInterval()
    {
        var policy = CreatePolicy();

        var result = policy.GetDelay(consecutiveFailures: 0);

        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact]
    public void GetDelay_WithFailures_IncreasesUntilMaximum()
    {
        var policy = CreatePolicy(jitterSeconds: 0);

        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(20), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(40), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(80), policy.GetDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(100), policy.GetDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(100), policy.GetDelay(30));
    }

    [Fact]
    public void GetDelay_ForSameDeviceAndFailureCount_IsStableAndBounded()
    {
        var firstPolicy = CreatePolicy(jitterSeconds: 10);
        var secondPolicy = CreatePolicy(jitterSeconds: 10);

        var first = firstPolicy.GetDelay(2);
        var second = secondPolicy.GetDelay(2);

        Assert.Equal(first, second);
        Assert.InRange(first, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
    }

    private static DispatchRetryPolicy CreatePolicy(int jitterSeconds = 5) =>
        new(new AgentOptions
        {
            DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DispatchIntervalSeconds = 10,
            MaxDispatchIntervalSeconds = 100,
            DispatchJitterSeconds = jitterSeconds
        });
}
