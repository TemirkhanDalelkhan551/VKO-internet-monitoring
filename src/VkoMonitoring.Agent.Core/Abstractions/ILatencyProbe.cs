using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Core.Abstractions;

public interface ILatencyProbe
{
    Task<LatencyMetrics> MeasureAsync(CancellationToken cancellationToken);
}
