using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Services;

public enum IncidentTransition
{
    None,
    Open,
    Resolve
}

public sealed record IncidentSignal(
    Guid EventId,
    DateTimeOffset MeasuredAtUtc,
    bool IsProblem);

public static class IncidentDetectionPolicy
{
    public static IncidentTransition DetermineTransition(
        IReadOnlyList<IncidentSignal> newestFirst,
        bool hasOpenIncident,
        IncidentDetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        ArgumentNullException.ThrowIfNull(options);

        if (newestFirst.Count == 0)
        {
            return IncidentTransition.None;
        }

        if (hasOpenIncident)
        {
            var recoveryCount = newestFirst.TakeWhile(signal => !signal.IsProblem).Count();
            return recoveryCount >= options.ConsecutiveRecoveryMeasurements
                ? IncidentTransition.Resolve
                : IncidentTransition.None;
        }

        var problemSignals = newestFirst.TakeWhile(signal => signal.IsProblem).ToArray();
        if (problemSignals.Length == 0)
        {
            return IncidentTransition.None;
        }

        var enoughMeasurements =
            problemSignals.Length >= options.ConsecutiveProblemMeasurements;
        var enoughDuration = options.MinimumViolationMinutes > 0 &&
            problemSignals[0].MeasuredAtUtc - problemSignals[^1].MeasuredAtUtc >=
            TimeSpan.FromMinutes(options.MinimumViolationMinutes);

        return enoughMeasurements || enoughDuration
            ? IncidentTransition.Open
            : IncidentTransition.None;
    }
}
