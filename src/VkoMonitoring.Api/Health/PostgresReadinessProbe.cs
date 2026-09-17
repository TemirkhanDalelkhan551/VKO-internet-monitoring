using System.Diagnostics;
using Npgsql;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Health;

public sealed class PostgresReadinessProbe(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options,
    TimeProvider timeProvider) : IApiReadinessProbe
{
    public async Task<ApiHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var dependencyAvailable = false;

        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1;");
            await command.ExecuteScalarAsync(cancellationToken);
            dependencyAvailable = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The health response intentionally does not expose database or network details.
        }

        stopwatch.Stop();
        var status = HealthStatusEvaluator.Evaluate(
            dependencyAvailable,
            stopwatch.Elapsed,
            TimeSpan.FromMilliseconds(options.ReadinessDegradedAfterMilliseconds));
        var durationMilliseconds = Math.Max(0, (long)stopwatch.Elapsed.TotalMilliseconds);

        return new ApiHealthReport(
            status.ToString(),
            timeProvider.GetUtcNow(),
            [new ApiHealthComponent("postgresql", status.ToString(), durationMilliseconds)]);
    }
}
