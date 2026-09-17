namespace VkoMonitoring.Agent.Core.Abstractions;

public interface IThroughputProbe
{
    Task<double> MeasureDownloadAsync(CancellationToken cancellationToken);
    Task<double> MeasureUploadAsync(CancellationToken cancellationToken);
}
