namespace VkoMonitoring.Agent.Core.Domain;

public sealed record SystemLoadSnapshot(
    double CpuUsagePercent,
    double NetworkMegabitsPerSecond);

public sealed record SystemLoadDecision(
    bool CanRunMeasurement,
    SystemLoadSnapshot Snapshot);
