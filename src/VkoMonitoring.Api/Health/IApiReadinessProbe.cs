namespace VkoMonitoring.Api.Health;

public interface IApiReadinessProbe
{
    Task<ApiHealthReport> CheckAsync(CancellationToken cancellationToken);
}
