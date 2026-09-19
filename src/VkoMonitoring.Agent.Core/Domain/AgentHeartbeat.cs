namespace VkoMonitoring.Agent.Core.Domain;

public sealed record AgentHeartbeat(
    Guid SchoolId,
    Guid DeviceId,
    Guid LineId,
    DateTimeOffset SentAtUtc,
    string AgentVersion);

public sealed record AgentRuntimeConfiguration(string[] MeasurementWindows);
