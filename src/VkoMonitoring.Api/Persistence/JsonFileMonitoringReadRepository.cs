using System.Text.Json;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;

namespace VkoMonitoring.Api.Persistence;

public sealed class JsonFileMonitoringReadRepository(
    MonitoringApiOptions options,
    TimeProvider timeProvider) : IMonitoringReadRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _measurementDirectory =
        Path.GetFullPath(Path.Combine(options.DataDirectory, "measurements"));
    private readonly string _deviceDirectory =
        Path.GetFullPath(Path.Combine(options.DataDirectory, "devices"));

    public async Task<IReadOnlyList<MeasurementReportRow>> GetReportRowsAsync(ReportFilter filter, int limit, CancellationToken cancellationToken)
    {
        var bindings = options.DeviceBindings.ToDictionary(pair => Guid.Parse(pair.Key), pair => pair.Value);
        return (await LoadMeasurementsAsync(cancellationToken)).Where(m =>
            m.MeasuredAtUtc >= filter.FromUtc && m.MeasuredAtUtc < filter.ToUtc &&
            (filter.SchoolId is null || m.SchoolId == filter.SchoolId) &&
            (filter.DeviceIds.Length == 0 || filter.DeviceIds.Contains(m.DeviceId)) &&
            (filter.ConnectionStatus is null || m.ConnectionStatus.ToString() == filter.ConnectionStatus) &&
            bindings.TryGetValue(m.DeviceId, out var binding) && binding.SchoolId == m.SchoolId && binding.LineId == m.LineId)
            .OrderBy(m => m.SchoolId).ThenBy(m => m.MeasuredAtUtc).ThenBy(m => m.EventId).Take(limit)
            .Select(m => new MeasurementReportRow(m.SchoolId, bindings[m.DeviceId].SchoolName ?? m.SchoolId.ToString(),
                m.DeviceId, bindings[m.DeviceId].DeviceName ?? m.DeviceId.ToString(), null, m.MeasuredAtUtc,
                m.DownloadMbps, m.UploadMbps, m.PingMilliseconds, m.JitterMilliseconds, m.PacketLossPercent,
                m.ConnectionStatus.ToString(), MonitoringStatusEvaluator.Evaluate(m, options.Thresholds) != MonitoringStatus.Normal)
                { LineId = m.LineId, LineName = bindings[m.DeviceId].LineName }).ToArray();
    }

    public async Task<IReadOnlyList<SchoolOverview>> GetSchoolsAsync(
        CancellationToken cancellationToken)
    {
        var measurements = await LoadMeasurementsAsync(cancellationToken);
        var heartbeats = await LoadHeartbeatsAsync(cancellationToken);
        var schools = new List<SchoolOverview>();

        foreach (var group in options.DeviceBindings.GroupBy(pair => pair.Value.SchoolId))
        {
            var firstBinding = group.First().Value;
            var now = timeProvider.GetUtcNow();
            var activeAfter = now - TimeSpan.FromMinutes(options.DeviceActiveWindowMinutes);
            var activeCount = group.Count(pair =>
                heartbeats.TryGetValue(Guid.Parse(pair.Key), out var heartbeat) &&
                heartbeat.SentAtUtc >= activeAfter);

            var lines = group.GroupBy(pair => pair.Value.LineId).Select(lineGroup =>
            {
                var binding = lineGroup.OrderBy(pair => pair.Key, StringComparer.Ordinal).First().Value;
                var latest = measurements.Where(measurement =>
                    measurement.SchoolId == group.Key && measurement.LineId == lineGroup.Key)
                    .OrderByDescending(measurement => measurement.MeasuredAtUtc)
                    .ThenBy(measurement => measurement.EventId).FirstOrDefault();
                var lineHeartbeats = lineGroup.Select(pair =>
                    heartbeats.GetValueOrDefault(Guid.Parse(pair.Key))).OfType<AgentHeartbeat>().ToArray();
                DateTimeOffset? lastSeen = lineHeartbeats.Length == 0 ? null :
                    lineHeartbeats.Max(heartbeat => heartbeat.SentAtUtc);
                return MonitoringOverviewFactory.WithCurrentState(new LineOverview(
                    group.Key, lineGroup.Key, RequiredOrFallback(binding.LineName, lineGroup.Key.ToString("D")),
                    binding.LineStatus, null, null, null, null,
                    lineGroup.Count(), lineHeartbeats.Count(heartbeat => heartbeat.SentAtUtc >= activeAfter),
                    lastSeen, MonitoringStatusEvaluator.Evaluate(latest, options.Thresholds), ToSnapshot(latest)),
                    now, options);
            }).OrderBy(line => line.Name, StringComparer.Ordinal).ThenBy(line => line.LineId).ToArray();

            schools.Add(MonitoringOverviewFactory.WithLines(new SchoolOverview(
                group.Key,
                RequiredOrFallback(firstBinding.SchoolName, group.Key.ToString("D")),
                null,
                null,
                null,
                null,
                null,
                null,
                group.Count(),
                activeCount,
                MonitoringStatus.Unknown,
                null), lines));
        }

        return schools.OrderBy(school => school.Name).ToArray();
    }

    public async Task<SchoolOverview?> GetSchoolAsync(
        Guid schoolId,
        CancellationToken cancellationToken) =>
        (await GetSchoolsAsync(cancellationToken))
        .SingleOrDefault(school => school.SchoolId == schoolId);

    public async Task<IReadOnlyList<DeviceOverview>> GetSchoolDevicesAsync(
        Guid schoolId,
        CancellationToken cancellationToken)
    {
        var measurements = await LoadMeasurementsAsync(cancellationToken);
        var heartbeats = await LoadHeartbeatsAsync(cancellationToken);
        var devices = new List<DeviceOverview>();

        foreach (var (deviceIdentifier, binding) in options.DeviceBindings)
        {
            if (binding.SchoolId != schoolId || !Guid.TryParse(deviceIdentifier, out var deviceId))
            {
                continue;
            }

            var latest = measurements
                .Where(measurement => measurement.DeviceId == deviceId &&
                    measurement.SchoolId == schoolId && measurement.LineId == binding.LineId)
                .OrderByDescending(measurement => measurement.MeasuredAtUtc)
                .FirstOrDefault();
            heartbeats.TryGetValue(deviceId, out var heartbeat);
            devices.Add(MonitoringOverviewFactory.WithCurrentState(new DeviceOverview(
                deviceId,
                binding.LineId,
                deviceIdentifier,
                RequiredOrFallback(binding.DeviceName, deviceIdentifier),
                null,
                null,
                heartbeat?.SentAtUtc,
                heartbeat?.AgentVersion,
                false,
                MonitoringStatusEvaluator.Evaluate(latest, options.Thresholds),
                ToSnapshot(latest)), timeProvider.GetUtcNow(), options));
        }

        return devices.OrderBy(device => device.Name).ToArray();
    }

    public async Task<DeviceOverview?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var binding = options.DeviceBindings
            .FirstOrDefault(pair =>
                Guid.TryParse(pair.Key, out var configuredDeviceId) &&
                configuredDeviceId == deviceId)
            .Value;
        if (binding is null)
        {
            return null;
        }

        return (await GetSchoolDevicesAsync(binding.SchoolId, cancellationToken))
            .SingleOrDefault(device => device.DeviceId == deviceId);
    }

    public async Task<IReadOnlyList<InternetMeasurement>> GetDeviceMeasurementsAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken) =>
        (await LoadMeasurementsAsync(cancellationToken))
        .Where(measurement =>
            measurement.DeviceId == deviceId &&
            measurement.MeasuredAtUtc >= fromUtc &&
            measurement.MeasuredAtUtc < toUtc)
        .OrderByDescending(measurement => measurement.MeasuredAtUtc)
        .Take(limit)
        .ToArray();

    public async Task<AnalyticsOverview> GetAnalyticsAsync(
        Guid? schoolId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var measurements = (await LoadMeasurementsAsync(cancellationToken))
            .Where(measurement =>
                measurement.MeasuredAtUtc >= fromUtc &&
                measurement.MeasuredAtUtc < toUtc &&
                (schoolId is null || measurement.SchoolId == schoolId))
            .ToArray();
        var problemCount = measurements.Count(measurement =>
            MonitoringStatusEvaluator.Evaluate(measurement, options.Thresholds) != MonitoringStatus.Normal);
        var availableCount = measurements.Count(measurement =>
            measurement.ConnectionStatus != ConnectionStatus.Offline);

        return new AnalyticsOverview(
            fromUtc,
            toUtc,
            measurements.Length,
            problemCount,
            Percentage(problemCount, measurements.Length),
            Percentage(availableCount, measurements.Length),
            Average(measurements.Select(item => item.DownloadMbps)),
            Minimum(measurements.Select(item => item.DownloadMbps)),
            Maximum(measurements.Select(item => item.DownloadMbps)),
            Average(measurements.Select(item => item.UploadMbps)),
            Minimum(measurements.Select(item => item.UploadMbps)),
            Maximum(measurements.Select(item => item.UploadMbps)),
            Average(measurements.Select(item => item.PingMilliseconds)),
            Minimum(measurements.Select(item => item.PingMilliseconds)),
            Maximum(measurements.Select(item => item.PingMilliseconds)));
    }

    private async Task<IReadOnlyList<InternetMeasurement>> LoadMeasurementsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_measurementDirectory))
        {
            return [];
        }

        var measurements = new List<InternetMeasurement>();
        foreach (var path in Directory.EnumerateFiles(_measurementDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var measurement = await JsonSerializer.DeserializeAsync<InternetMeasurement>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (measurement is not null)
            {
                measurements.Add(measurement);
            }
        }

        return measurements;
    }

    private async Task<IReadOnlyDictionary<Guid, AgentHeartbeat>> LoadHeartbeatsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_deviceDirectory))
        {
            return new Dictionary<Guid, AgentHeartbeat>();
        }

        var heartbeats = new Dictionary<Guid, AgentHeartbeat>();
        foreach (var path in Directory.EnumerateFiles(_deviceDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var heartbeat = await JsonSerializer.DeserializeAsync<AgentHeartbeat>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (heartbeat is not null)
            {
                heartbeats[heartbeat.DeviceId] = heartbeat;
            }
        }

        return heartbeats;
    }

    private static MeasurementSnapshot? ToSnapshot(InternetMeasurement? measurement) =>
        measurement is null
            ? null
            : new MeasurementSnapshot(
                measurement.DownloadMbps,
                measurement.UploadMbps,
                measurement.PingMilliseconds,
                measurement.JitterMilliseconds,
                measurement.PacketLossPercent,
                measurement.MeasuredAtUtc);

    private static string RequiredOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.OfType<double>().ToArray();
        return present.Length == 0 ? null : Math.Round(present.Average(), 3);
    }

    private static double? Minimum(IEnumerable<double?> values)
    {
        var present = values.OfType<double>().ToArray();
        return present.Length == 0 ? null : present.Min();
    }

    private static double? Maximum(IEnumerable<double?> values)
    {
        var present = values.OfType<double>().ToArray();
        return present.Length == 0 ? null : present.Max();
    }

    private static double Percentage(int part, int total) =>
        total == 0 ? 0 : Math.Round(part * 100d / total, 2);
}
