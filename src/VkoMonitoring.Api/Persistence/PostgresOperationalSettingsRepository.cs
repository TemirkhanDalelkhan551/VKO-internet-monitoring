using Npgsql;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresOperationalSettingsRepository(NpgsqlDataSource dataSource, MonitoringApiOptions options)
    : IOperationalSettingsRepository
{
    public async Task<OperationalSettings> GetAsync(CancellationToken cancellationToken)
    {
        const string sql = """SELECT measurement_windows, minimum_download_mbps, minimum_upload_mbps, maximum_ping_milliseconds, maximum_jitter_milliseconds, maximum_packet_loss_percent, minimum_availability_percent, updated_at_utc FROM operational_settings WHERE singleton = true;""";
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : ConfiguredOperationalSettingsRepository.ToSettings(options);
    }

    public async Task<OperationalSettings> UpdateAsync(OperationalSettings value, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operational_settings (singleton, measurement_windows, minimum_download_mbps, minimum_upload_mbps, maximum_ping_milliseconds, maximum_jitter_milliseconds, maximum_packet_loss_percent, minimum_availability_percent, updated_at_utc)
            VALUES (true, $1, $2, $3, $4, $5, $6, $7, now())
            ON CONFLICT (singleton) DO UPDATE SET measurement_windows=$1, minimum_download_mbps=$2, minimum_upload_mbps=$3, maximum_ping_milliseconds=$4, maximum_jitter_milliseconds=$5, maximum_packet_loss_percent=$6, minimum_availability_percent=$7, updated_at_utc=now()
            RETURNING measurement_windows, minimum_download_mbps, minimum_upload_mbps, maximum_ping_milliseconds, maximum_jitter_milliseconds, maximum_packet_loss_percent, minimum_availability_percent, updated_at_utc;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(value.MeasurementWindows); command.Parameters.AddWithValue(value.MinimumDownloadMbps);
        command.Parameters.AddWithValue(value.MinimumUploadMbps); command.Parameters.AddWithValue(value.MaximumPingMilliseconds);
        command.Parameters.AddWithValue(value.MaximumJitterMilliseconds); command.Parameters.AddWithValue(value.MaximumPacketLossPercent);
        command.Parameters.AddWithValue(value.MinimumAvailabilityPercent);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); return Read(reader);
    }

    private static OperationalSettings Read(NpgsqlDataReader reader) => new(
        reader.GetFieldValue<string[]>(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3),
        reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetFieldValue<DateTimeOffset>(7));
}
