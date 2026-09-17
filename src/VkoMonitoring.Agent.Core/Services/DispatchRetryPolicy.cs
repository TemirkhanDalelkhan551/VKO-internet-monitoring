using System.Security.Cryptography;
using System.Text;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Core.Services;

public sealed class DispatchRetryPolicy
{
    private readonly Guid _deviceId;
    private readonly int _baseDelaySeconds;
    private readonly int _maximumDelaySeconds;
    private readonly int _maximumJitterSeconds;

    public DispatchRetryPolicy(AgentOptions options)
    {
        _deviceId = options.DeviceId;
        _baseDelaySeconds = options.DispatchIntervalSeconds;
        _maximumDelaySeconds = options.MaxDispatchIntervalSeconds;
        _maximumJitterSeconds = options.DispatchJitterSeconds;
    }

    public TimeSpan GetDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.FromSeconds(_baseDelaySeconds);
        }

        var exponent = Math.Min(consecutiveFailures - 1, 30);
        var exponentialDelay = (long)_baseDelaySeconds << exponent;
        var cappedDelay = Math.Min(exponentialDelay, _maximumDelaySeconds);
        var availableJitter = Math.Min(_maximumJitterSeconds, _maximumDelaySeconds - (int)cappedDelay);
        var jitter = CalculateJitter(consecutiveFailures, availableJitter);

        return TimeSpan.FromSeconds(cappedDelay + jitter);
    }

    private int CalculateJitter(int consecutiveFailures, int maximumJitter)
    {
        if (maximumJitter <= 0)
        {
            return 0;
        }

        var input = Encoding.UTF8.GetBytes($"{_deviceId:N}:{consecutiveFailures}");
        var hash = SHA256.HashData(input);
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)(maximumJitter + 1));
    }
}
