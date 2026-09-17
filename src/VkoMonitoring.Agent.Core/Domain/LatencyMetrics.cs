namespace VkoMonitoring.Agent.Core.Domain;

public sealed record LatencyMetrics(
    double AverageMilliseconds,
    double JitterMilliseconds,
    double PacketLossPercent);
