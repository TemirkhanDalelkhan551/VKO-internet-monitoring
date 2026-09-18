using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Api.Security;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresMonitoringReadRepository(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options,
    TimeProvider timeProvider,
    MonitoringAccessContext access) : IMonitoringReadRepository
{
    private const string SchoolSelect = """
        SELECT s.id, s.name, s.district_city, s.address,
               (SELECT COUNT(*)::int FROM devices d WHERE d.school_id = s.id AND /* device access */),
               (SELECT COUNT(*)::int FROM devices d
                WHERE d.school_id = s.id
                  AND /* device access */
                  AND d.is_blocked = false
                  AND d.last_seen_at_utc >= now() - ($1 * interval '1 minute'))
        FROM schools s
        WHERE /* school access */
        """;

    private string ScopedSchoolSelect => SchoolSelect.Replace("/* device access */", access.SqlCondition("d.line_id"))
        .Replace("/* school access */", access.Current?.AllowedLineIds is null ? "true" :
            $"EXISTS (SELECT 1 FROM internet_lines l WHERE l.school_id=s.id AND {access.SqlCondition("l.id")})");

    public async Task<IReadOnlyList<MeasurementReportRow>> GetReportRowsAsync(
        ReportFilter filter, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT s.id, s.name, d.id, d.name, d.room, m.measured_at_utc,
                   m.download_mbps, m.upload_mbps, m.ping_milliseconds,
                   m.jitter_milliseconds, m.packet_loss_percent, m.connection_status,
                   (m.connection_status <> 'Online'
                    OR COALESCE(m.download_mbps < m.threshold_download_mbps, false)
                    OR COALESCE(m.upload_mbps < m.threshold_upload_mbps, false)
                    OR COALESCE(m.ping_milliseconds > m.threshold_ping_milliseconds, false)
                    OR COALESCE(m.jitter_milliseconds > m.threshold_jitter_milliseconds, false)
                    OR COALESCE(m.packet_loss_percent > m.threshold_packet_loss_percent, false))
            FROM measurements m
            JOIN schools s ON s.id = m.school_id
            JOIN devices d ON d.id = m.device_id AND d.school_id = m.school_id AND d.line_id = m.line_id
            WHERE m.measured_at_utc >= $1 AND m.measured_at_utc < $2
              AND ($3::uuid IS NULL OR m.school_id = $3)
              AND (cardinality($4::uuid[]) = 0 OR m.device_id = ANY($4))
              AND ($5::text IS NULL OR m.connection_status = $5)
              AND /* access */
            ORDER BY s.name, m.measured_at_utc, m.event_id
            LIMIT $6;
            """;
        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("m.line_id")));
        command.Parameters.AddWithValue(filter.FromUtc); command.Parameters.AddWithValue(filter.ToUtc);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = filter.SchoolId is null ? DBNull.Value : filter.SchoolId.Value });
        command.Parameters.AddWithValue(filter.DeviceIds);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)filter.ConnectionStatus ?? DBNull.Value });
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MeasurementReportRow>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add(new MeasurementReportRow(
            reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), GetNullableString(reader, 4),
            reader.GetFieldValue<DateTimeOffset>(5), GetNullableDouble(reader, 6), GetNullableDouble(reader, 7),
            GetNullableDouble(reader, 8), GetNullableDouble(reader, 9), GetNullableDouble(reader, 10), reader.GetString(11), reader.GetBoolean(12)));
        return rows;
    }

    public async Task<IReadOnlyList<SchoolOverview>> GetSchoolsAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ScopedSchoolSelect + " ORDER BY s.name;");
        command.Parameters.AddWithValue(options.DeviceActiveWindowMinutes);
        return await ReadSchoolsAsync(command, cancellationToken);
    }

    public async Task<SchoolOverview?> GetSchoolAsync(
        Guid schoolId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(ScopedSchoolSelect + " AND s.id = $2;");
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
                WHERE m.device_id = d.id AND m.line_id = d.line_id AND m.school_id = d.school_id
                ORDER BY m.measured_at_utc DESC
                LIMIT 1
            ) latest ON true
            WHERE d.school_id = $1
              AND /* access */
            ORDER BY d.name;
            """;

        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("d.line_id")));
        command.Parameters.AddWithValue(schoolId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var devices = new List<DeviceOverview>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var deviceId = reader.GetGuid(0);
            var lineId = reader.GetGuid(1);
            var measurement = ReadLatestMeasurement(reader, 9, schoolId, deviceId, lineId);
            devices.Add(MonitoringOverviewFactory.WithCurrentState(new DeviceOverview(
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
                ToSnapshot(measurement)), timeProvider.GetUtcNow(), options));
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
              AND /* access */
              AND measured_at_utc >= $2
              AND measured_at_utc < $3
            ORDER BY measured_at_utc DESC
            LIMIT $4;
            """;

        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("line_id")));
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
              AND ($3::uuid IS NULL OR school_id = $3)
              AND /* access */;
            """;

        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("line_id")));
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
        var schools = new List<SchoolOverview>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                schools.Add(new SchoolOverview(
                    reader.GetGuid(0), reader.GetString(1),
                    GetNullableString(reader, 2), GetNullableString(reader, 3),
                    null, null, null, null, reader.GetInt32(4), reader.GetInt32(5),
                    MonitoringStatus.Unknown, null));
            }
        }

        if (schools.Count == 0)
        {
            return schools;
        }

        var lines = await GetLinesAsync(schools.Count == 1 ? schools[0].SchoolId : null, cancellationToken);
        var linesBySchool = lines.ToLookup(line => line.SchoolId);
        return schools.Select(school => MonitoringOverviewFactory.WithLines(
            school, linesBySchool[school.SchoolId].ToArray())).ToArray();
    }

    private async Task<IReadOnlyList<LineOverview>> GetLinesAsync(
        Guid? schoolId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.school_id, l.id, l.name, l.status, l.provider_name, l.connection_type,
                   l.contracted_download_mbps, l.contracted_upload_mbps,
                   presence.device_count, presence.active_count, presence.last_seen,
                   latest.event_id, latest.measured_at_utc, latest.download_mbps,
                   latest.upload_mbps, latest.ping_milliseconds, latest.jitter_milliseconds,
                   latest.packet_loss_percent, latest.connection_status, latest.device_id
            FROM internet_lines l
            LEFT JOIN LATERAL (
                SELECT COUNT(*)::int AS device_count,
                       COUNT(*) FILTER (WHERE NOT d.is_blocked AND d.last_seen_at_utc >= $1)::int AS active_count,
                       MAX(d.last_seen_at_utc) FILTER (WHERE NOT d.is_blocked) AS last_seen
                FROM devices d WHERE d.line_id = l.id AND d.school_id = l.school_id
            ) presence ON true
            LEFT JOIN LATERAL (
                SELECT m.event_id, m.device_id, m.measured_at_utc, m.download_mbps,
                       m.upload_mbps, m.ping_milliseconds, m.jitter_milliseconds,
                       m.packet_loss_percent, m.connection_status
                FROM measurements m
                WHERE m.line_id = l.id AND m.school_id = l.school_id
                ORDER BY m.measured_at_utc DESC, m.received_at_utc DESC, m.event_id
                LIMIT 1
            ) latest ON true
            WHERE ($2::uuid IS NULL OR l.school_id = $2) AND /* access */
            ORDER BY l.school_id, l.name, l.id;
            """;
        var now = timeProvider.GetUtcNow();
        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("l.id")));
        command.Parameters.AddWithValue(now.AddMinutes(-options.DeviceActiveWindowMinutes));
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = schoolId is null ? DBNull.Value : schoolId.Value
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var lines = new List<LineOverview>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var lineSchoolId = reader.GetGuid(0);
            var lineId = reader.GetGuid(1);
            var measurement = ReadLatestMeasurement(reader, 11, lineSchoolId,
                reader.IsDBNull(19) ? Guid.Empty : reader.GetGuid(19), lineId);
            lines.Add(MonitoringOverviewFactory.WithCurrentState(new LineOverview(
                lineSchoolId, lineId, reader.GetString(2), reader.GetString(3),
                GetNullableString(reader, 4), GetNullableString(reader, 5),
                GetNullableDouble(reader, 6), GetNullableDouble(reader, 7),
                reader.GetInt32(8), reader.GetInt32(9), GetNullableDateTimeOffset(reader, 10),
                MonitoringStatusEvaluator.Evaluate(measurement, options.Thresholds),
                ToSnapshot(measurement)), now, options));
        }

        return lines;
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
