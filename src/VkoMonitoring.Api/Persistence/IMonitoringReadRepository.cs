using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public interface IMonitoringReadRepository
{
    Task<IReadOnlyList<MeasurementReportRow>> GetReportRowsAsync(ReportFilter filter, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<SchoolOverview>> GetSchoolsAsync(CancellationToken cancellationToken);
    Task<SchoolOverview?> GetSchoolAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<DeviceOverview?> GetDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceOverview>> GetSchoolDevicesAsync(Guid schoolId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InternetMeasurement>> GetDeviceMeasurementsAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken);
    Task<AnalyticsOverview> GetAnalyticsAsync(
        Guid? schoolId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
