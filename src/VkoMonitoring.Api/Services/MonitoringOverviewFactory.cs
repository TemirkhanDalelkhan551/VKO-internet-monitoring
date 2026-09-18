using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Services;

public static class MonitoringOverviewFactory
{
    public static MeasurementFreshness GetFreshness(
        MeasurementSnapshot? measurement, DateTimeOffset now, MonitoringApiOptions options) =>
        measurement is null ? MeasurementFreshness.Missing :
        measurement.MeasuredAtUtc < now.AddMinutes(-options.MeasurementFreshnessMinutes)
            ? MeasurementFreshness.Stale : MeasurementFreshness.Fresh;

    public static AgentPresence GetPresence(
        DateTimeOffset? lastSeen, bool blocked, DateTimeOffset now, MonitoringApiOptions options) =>
        blocked ? AgentPresence.Blocked :
        lastSeen is null ? AgentPresence.NotSeen :
        lastSeen >= now.AddMinutes(-options.DeviceActiveWindowMinutes)
            ? AgentPresence.Active : AgentPresence.Inactive;

    public static DeviceOverview WithCurrentState(
        DeviceOverview device, DateTimeOffset now, MonitoringApiOptions options)
    {
        var freshness = GetFreshness(device.LatestMeasurement, now, options);
        return device with
        {
            QualityStatus = device.Status,
            MeasurementFreshness = freshness,
            AgentPresence = GetPresence(device.LastSeenAtUtc, device.IsBlocked, now, options),
            Status = freshness == MeasurementFreshness.Fresh && !device.IsBlocked
                ? device.Status : MonitoringStatus.Unknown
        };
    }

    public static LineOverview WithCurrentState(
        LineOverview line, DateTimeOffset now, MonitoringApiOptions options)
    {
        var freshness = GetFreshness(line.LatestMeasurement, now, options);
        return line with
        {
            QualityStatus = line.Status,
            MeasurementFreshness = freshness,
            AgentPresence = GetPresence(line.LastSeenAtUtc, false, now, options),
            Status = freshness == MeasurementFreshness.Fresh && line.LineStatus != "Disabled"
                ? line.Status : MonitoringStatus.Unknown
        };
    }

    public static SchoolOverview WithLines(SchoolOverview school, IReadOnlyList<LineOverview> lines)
    {
        var primary = lines.Where(line => line.LineStatus == "Primary")
            .OrderBy(line => line.LineId).FirstOrDefault();
        return school with
        {
            PrimaryLineId = primary?.LineId,
            ProviderName = primary?.ProviderName,
            ConnectionType = primary?.ConnectionType,
            ContractedDownloadMbps = primary?.ContractedDownloadMbps,
            ContractedUploadMbps = primary?.ContractedUploadMbps,
            Status = primary?.Status ?? MonitoringStatus.Unknown,
            QualityStatus = primary?.QualityStatus ?? MonitoringStatus.Unknown,
            MeasurementFreshness = primary?.MeasurementFreshness ?? MeasurementFreshness.Missing,
            AgentPresence = primary?.AgentPresence ?? AgentPresence.NotSeen,
            LatestMeasurement = primary?.LatestMeasurement,
            Lines = lines
        };
    }
}
