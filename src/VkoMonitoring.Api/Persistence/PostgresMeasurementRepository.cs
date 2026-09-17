using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresMeasurementRepository(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options) : IMeasurementRepository
{
    public async Task<bool> AddIfNotExistsAsync(
        InternetMeasurement measurement,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO measurements (
                event_id, school_id, device_id, line_id, measured_at_utc,
                download_mbps, upload_mbps, ping_milliseconds, jitter_milliseconds,
                packet_loss_percent, connection_status, failure_kind, failure_reason,
                agent_version, duration_milliseconds, external_ip_address,
                network_connection_type, measurement_server,
                threshold_download_mbps, threshold_upload_mbps,
                threshold_ping_milliseconds, threshold_jitter_milliseconds,
                threshold_packet_loss_percent)
            VALUES (
                $1, $2, $3, $4, $5,
                $6, $7, $8, $9,
                $10, $11, $12, $13,
                $14, $15, $16,
                $17, $18, $19,
                $20, $21, $22, $23)
            ON CONFLICT (event_id) DO NOTHING;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(measurement.EventId);
        command.Parameters.AddWithValue(measurement.SchoolId);
        command.Parameters.AddWithValue(measurement.DeviceId);
        command.Parameters.AddWithValue(measurement.LineId);
        command.Parameters.AddWithValue(measurement.MeasuredAtUtc);
        AddNullableDecimal(command, measurement.DownloadMbps);
        AddNullableDecimal(command, measurement.UploadMbps);
        AddNullableDecimal(command, measurement.PingMilliseconds);
        AddNullableDecimal(command, measurement.JitterMilliseconds);
        AddNullableDecimal(command, measurement.PacketLossPercent);
        command.Parameters.AddWithValue(measurement.ConnectionStatus.ToString());
        command.Parameters.AddWithValue(measurement.FailureKind.ToString());
        command.Parameters.AddWithValue((object?)measurement.FailureReason ?? DBNull.Value);
        command.Parameters.AddWithValue(measurement.AgentVersion);
        command.Parameters.AddWithValue(measurement.DurationMilliseconds);
        command.Parameters.AddWithValue((object?)measurement.ExternalIpAddress ?? DBNull.Value);
        command.Parameters.AddWithValue(measurement.NetworkConnectionType.ToString());
        command.Parameters.AddWithValue((object?)measurement.MeasurementServer ?? DBNull.Value);
        command.Parameters.AddWithValue(options.Thresholds.MinimumDownloadMbps);
        command.Parameters.AddWithValue(options.Thresholds.MinimumUploadMbps);
        command.Parameters.AddWithValue(options.Thresholds.MaximumPingMilliseconds);
        command.Parameters.AddWithValue(options.Thresholds.MaximumJitterMilliseconds);
        command.Parameters.AddWithValue(options.Thresholds.MaximumPacketLossPercent);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<InternetMeasurement>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_id, school_id, device_id, line_id, measured_at_utc,
                   download_mbps, upload_mbps, ping_milliseconds, jitter_milliseconds,
                   packet_loss_percent, connection_status, failure_reason, agent_version,
                   failure_kind, duration_milliseconds, external_ip_address,
                   network_connection_type, measurement_server
            FROM measurements
            ORDER BY measured_at_utc DESC
            LIMIT $1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var measurements = new List<InternetMeasurement>(limit);

        while (await reader.ReadAsync(cancellationToken))
        {
            measurements.Add(new InternetMeasurement(
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
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12))
            {
                FailureKind = Enum.Parse<MeasurementFailureKind>(reader.GetString(13)),
                DurationMilliseconds = reader.GetInt64(14),
                ExternalIpAddress = reader.IsDBNull(15) ? null : reader.GetString(15),
                NetworkConnectionType = Enum.Parse<NetworkConnectionType>(reader.GetString(16)),
                MeasurementServer = reader.IsDBNull(17) ? null : reader.GetString(17)
            });
        }

        return measurements;
    }

    private static void AddNullableDecimal(NpgsqlCommand command, double? value)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Numeric,
            Value = value is null ? DBNull.Value : Convert.ToDecimal(value.Value)
        });
    }

    private static double? GetNullableDouble(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetDecimal(ordinal));
}
