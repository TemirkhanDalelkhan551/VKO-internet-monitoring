namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IMeasurementSchedule
{
    DateTimeOffset GetNextRun(DateTimeOffset now);
}
