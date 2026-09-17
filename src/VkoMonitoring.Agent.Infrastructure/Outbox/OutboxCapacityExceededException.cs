namespace VkoMonitoring.Agent.Infrastructure.Outbox;

public sealed class OutboxCapacityExceededException(int maximumMeasurements)
    : IOException($"The measurement outbox reached its limit of {maximumMeasurements} items.");
