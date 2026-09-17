namespace VkoMonitoring.Api.Health;

public sealed class LocalStorageReadinessProbe(TimeProvider timeProvider) : IApiReadinessProbe
{
    public Task<ApiHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = ApiHealthStatus.Healthy.ToString();
        return Task.FromResult(new ApiHealthReport(
            status,
            timeProvider.GetUtcNow(),
            [new ApiHealthComponent("local-storage", status, 0)]));
    }
}
