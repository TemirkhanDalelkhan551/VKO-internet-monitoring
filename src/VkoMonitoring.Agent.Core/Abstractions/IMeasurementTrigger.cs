namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IMeasurementTrigger
{
    Task<bool> WaitForRequestAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public static class MeasurementTriggerFile
{
    public const string Name = "measurement-now.request";
}
