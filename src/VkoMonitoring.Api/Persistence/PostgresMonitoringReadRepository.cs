using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresMonitoringReadRepository(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options) : IMonitoringReadRepository
{
    private const string SchoolSelect = """
        SELECT s.id, s.name, s.district_city, s.address,
               line.provider_name, line.connection_type,
               line.contracted_download_mbps, line.contracted_upload_mbps,
               (SELECT COUNT(*)::int FROM devices d WHERE d.school_id = s.id),
               (SELECT COUNT(*)::int FROM devices d
                WHERE d.school_id = s.id
                  AND d.is_blocked = false
                  AND d.last_seen_at_utc >= now() - ($1 * interval '1 minute')),
               latest.event_id, latest.measured_at_utc,
               latest.download_mbps, latest.upload_mbps, latest.ping_milliseconds,
               latest.jitter_milliseconds, latest.packet_loss_percent,
               latest.connection_status, latest.device_id, latest.line_id
        FROM schools s
        LEFT JOIN LATERAL (
            SELECT l.provider_name, l.connection_type,
                   l.contracted_download_mbps, l.contracted_upload_mbps
            FROM internet_lines l
            WHERE l.school_id = s.id
            ORDER BY CASE l.status WHEN 'Primary' THEN 0 WHEN 'Backup' THEN 1 ELSE 2 END,
                     l.created_at_utc
            LIMIT 1
        ) line ON true
        LEFT JOIN LATERAL (
            SELECT m.event_id, m.device_id, m.line_id, m.measured_at_utc,
                   m.download_mbps, m.upload_mbps, m.ping_milliseconds,
                   m.jitter_milliseconds, m.packet_loss_percent,
                   m.connection_status
            FROM measurements m
            WHERE m.school_id = s.id
            ORDER BY m.measured_at_utc DESC
            LIMIT 1
        ) latest ON true
        """;

    public async Task<IReadOnlyList<SchoolOverview>> GetSchoolsAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SchoolSelect + " ORDER BY s.name;");
        command.Parameters.AddWithValue(options.DeviceActiveWindowMinutes);
        return await ReadSchoolsAsync(command, cancellationToken);
    }

    public async Task<SchoolOverview?> GetSchoolAsync(
        Guid schoolId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(SchoolSelect + " WHERE s.id = $2;");
        command.Parameters.AddWithValue(options.DeviceActiveWindowMinutes);
        command.Parameters.AddWithValue(schoolId);
        return (await ReadSchoolsAsync(command, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<DeviceOverview>> GetSchoolDevicesAsync(
        Guid schoolId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.id, d.line_id, d.device_identifier, d.name, d.room,
                   d.connection_type, d.last_seen_at_utc, d.agent_version, d.is_blocked,
                   latest.event_id, latest.measured_at_utc, latest.download_mbps,
                   latest.upload_mbps, latest.ping_milliseconds,
                   latest.jitter_milliseconds, latest.packet_loss_percent,
                   latest.connection_status
            FROM devices d
            LEFT JOIN LATERAL (
                SELECT m.event_id, m.measured_at_utc, m.download_mbps,
                       m.upload_mbps, m.ping_milliseconds,
                       m.jitter_milliseconds, m.packet_loss_percent,
                       m.connection_status
                FROM measurements m
                WHERE m.device_id = d.id
                ORDER BY m.measured_at_utc DESC
                LIMIT 1
            ) latest ON true
            WHERE d.school_id = $1
            ORDER BY d.name;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(schoolId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var devices = new List<DeviceOverview>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var deviceId = reader.GetGuid(0);
            var lineId = reader.GetGuid(1);
            var measurement = ReadLatestMeasurement(reader, 9, schoolId, deviceId, lineId);
            devices.Add(new DeviceOverview(
                deviceId,
                lineId,
                reader.GetString(2),
                reader.GetString(3),
                GetNullableString(reader, 4),
                GetNullableString(reader, 5),
                GetNullableDateTimeOffset(reader, 6),
                GetNullableString(reader, 7),
                reader.GetBoolean(8),
                MonitoringStatusEvaluator.Evaluate(measurement, options.Thresholds),
                ToSnapshot(measurement)));
        }

        return devices;
    }

    public async Task<DeviceOverview?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT school_id FROM devices WHERE id = $1;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        var schoolIdValue = await command.ExecuteScalarAsync(cancellationToken);
        if (schoolIdValue is not Guid schoolId)
        {
            return null;
        }

        return (await GetSchoolDevicesAsync(schoolId, cancellationToken))
            .SingleOrDefault(device => device.DeviceId == deviceId);
    }

    public async Task<IReadOnlyList<InternetMeasurement>> GetDeviceMeasurementsAsync(
        Guid deviceId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_id, school_id, device_id, line_id, measured_at_utc,
                   download_mbps, upload_mbps, ping_milliseconds, jitter_milliseconds,
                   packet_loss_percent, connection_status, failure_reason,
                   agent_version, failure_kind, duration_milliseconds,
                   external_ip_address, network_connection_type, measurement_server
            FROM measurements
            WHERE device_id = $1
              AND measured_at_utc >= $2
              AND measured_at_utc < $3
            ORDER BY measured_at_utc DESC
            LIMIT $4;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(fromUtc);
        command.Parameters.AddWithValue(toUtc);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var measurements = new List<InternetMeasurement>(limit);

        while (await reader.ReadAsync(cancellationToken))
        {
            measurements.Add(ReadMeasurement(reader));
        }

        return measurements;
    }

    public async Task<AnalyticsOverview> GetAnalyticsAsync(
        Guid? schoolId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)::int,
                   COUNT(*) FILTER (
                       WHERE connection_status <> 'Online'
                          OR download_mbps < threshold_download_mbps
                          OR upload_mbps < threshold_upload_mbps
                          OR ping_milliseconds > threshold_ping_milliseconds
                          OR jitter_milliseconds > threshold_jitter_milliseconds
                          OR packet_loss_percent > threshold_packet_loss_percent)::int,
                   COUNT(*) FILTER (WHERE connection_status <> 'Offline')::int,
                   AVG(download_mbps), MIN(download_mbps), MAX(download_mbps),
                   AVG(upload_mbps), MIN(upload_mbps), MAX(upload_mbps),
                   AVG(ping_milliseconds), MIN(ping_milliseconds), MAX(ping_milliseconds)
            FROM measurements
            WHERE measured_at_utc >= $1
              AND measured_at_utc < $2
              AND ($3::uuid IS NULL OR school_id = $3);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(fromUtc);
        command.Parameters.AddWithValue(toUtc);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = schoolId is null ? DBNull.Value : schoolId.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var count = reader.GetInt32(0);
        var problemCount = reader.GetInt32(1);
        var availableCount = reader.GetInt32(2);
        return new AnalyticsOverview(
            fromUtc,
            toUtc,
            count,
            problemCount,
            Percentage(problemCount, count),
            Percentage(availableCount, count),
            GetNullableDouble(reader, 3),
            GetNullableDouble(reader, 4),
            GetNullableDouble(reader, 5),
            GetNullableDouble(reader, 6),
            GetNullableDouble(reader, 7),
            GetNullableDouble(reader, 8),
            GetNullableDouble(reader, 9),
            GetNullableDouble(reader, 10),
            GetNullableDouble(reader, 11));
    }

    private async Task<IReadOnlyList<SchoolOverview>> ReadSchoolsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var schools = new List<SchoolOverview>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var schoolId = reader.GetGuid(0);
            var measurement = ReadLatestMeasurement(
                reader,
                10,
                schoolId,
                reader.IsDBNull(18) ? Guid.Empty : reader.GetGuid(18),
                reader.IsDBNull(19) ? Guid.Empty : reader.GetGuid(19));
            schools.Add(new SchoolOverview(
                schoolId,
                reader.GetString(1),
                GetNullableString(reader, 2),
                GetNullableString(reader, 3),
                GetNullableString(reader, 4),
                GetNullableString(reader, 5),
                GetNullableDouble(reader, 6),
                GetNullableDouble(reader, 7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                MonitoringStatusEvaluator.Evaluate(measurement, options.Thresholds),
                ToSnapshot(measurement)));
        }

        return schools;
    }

    private static InternetMeasurement? ReadLatestMeasurement(
        NpgsqlDataReader reader,
        int start,
        Guid schoolId,
        Guid deviceId,
        Guid lineId)
    {
        if (reader.IsDBNull(start))
        {
            return null;
        }

        return new InternetMeasurement(
            reader.GetGuid(start),
            schoolId,
            deviceId,
            lineId,
            new DateTimeOffset(reader.GetDateTime(start + 1)),
            GetNullableDouble(reader, start + 2),
            GetNullableDouble(reader, start + 3),
            GetNullableDouble(reader, start + 4),
            GetNullableDouble(reader, start + 5),
            GetNullableDouble(reader, start + 6),
            Enum.Parse<ConnectionStatus>(reader.GetString(start + 7)),
            null,
            "server");
    }

    private static InternetMeasurement ReadMeasurement(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            new DateTimeOffset(reader.GetDateTime(4)),
            GetNullableDouble(reader, 5),
            GetNullableDouble(reader, 6),
            GetNullableDouble(reader, 7),
            GetNullableDouble(reader, 8),
            GetNullableDouble(reader, 9),
            Enum.Parse<ConnectionStatus>(reader.GetString(10)),
            GetNullableString(reader, 11),
            reader.GetString(12))
        {
            FailureKind = Enum.Parse<MeasurementFailureKind>(reader.GetString(13)),
            DurationMilliseconds = reader.GetInt64(14),
            ExternalIpAddress = reader.IsDBNull(15) ? null : reader.GetString(15),
            NetworkConnectionType = Enum.Parse<NetworkConnectionType>(reader.GetString(16)),
            MeasurementServer = reader.IsDBNull(17) ? null : reader.GetString(17)
        };

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

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new DateTimeOffset(reader.GetDateTime(ordinal));

    private static double? GetNullableDouble(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetDecimal(ordinal));

    private static double Percentage(int part, int total) =>
        total == 0 ? 0 : Math.Round(part * 100d / total, 2);
}
