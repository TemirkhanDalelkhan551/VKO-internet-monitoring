namespace VkoMonitoring.Api.Health;

public static class HealthStatusEvaluator
{
    public static ApiHealthStatus Evaluate(
        bool dependencyAvailable,
        TimeSpan duration,
        TimeSpan degradedAfter)
    {
        if (!dependencyAvailable)
        {
            return ApiHealthStatus.Unavailable;
        }

        return duration >= degradedAfter
            ? ApiHealthStatus.Degraded
            : ApiHealthStatus.Healthy;
    }
}
