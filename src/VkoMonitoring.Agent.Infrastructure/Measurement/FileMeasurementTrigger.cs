using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Infrastructure.Measurement;

public sealed class FileMeasurementTrigger : IMeasurementTrigger
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly string requestPath;
    private readonly TimeProvider timeProvider;

    public FileMeasurementTrigger(AgentOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.timeProvider = timeProvider;
        var dataDirectory = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(options.DataDirectory));
        Directory.CreateDirectory(dataDirectory);
        requestPath = Path.Combine(dataDirectory, MeasurementTriggerFile.Name);
    }

    public async Task<bool> WaitForRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var deadline = timeProvider.GetUtcNow() + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryConsumeRequest())
            {
                return true;
            }

            var remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                timeProvider,
                cancellationToken);
        }
    }

    private bool TryConsumeRequest()
    {
        try
        {
            if (!File.Exists(requestPath))
            {
                return false;
            }

            File.Delete(requestPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
