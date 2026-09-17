using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDeviceRegistrationRepository(NpgsqlDataSource dataSource)
    : IDeviceRegistrationRepository
{
    public async Task RegisterAsync(
        DeviceRegistrationRequest request,
        Guid schoolId,
        Guid lineId,
        Guid deviceId,
        string deviceIdentifier,
        byte[] tokenHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await UpsertSchoolAsync(connection, transaction, request, schoolId, cancellationToken);
        await UpsertLineAsync(connection, transaction, request, schoolId, lineId, cancellationToken);
        await UpsertDeviceAsync(
            connection,
            transaction,
            request,
            schoolId,
            lineId,
            deviceId,
            deviceIdentifier,
            tokenHash,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertSchoolAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceRegistrationRequest request,
        Guid schoolId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO schools (id, name, district_city, address)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                district_city = EXCLUDED.district_city,
                address = EXCLUDED.address,
                updated_at_utc = now();
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.AddWithValue(request.SchoolName.Trim());
        AddNullableText(command, request.DistrictCity);
        AddNullableText(command, request.Address);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceRegistrationRequest request,
        Guid schoolId,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO internet_lines (
                id, school_id, name, provider_name, connection_type,
                contracted_download_mbps, contracted_upload_mbps, status)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (id) DO UPDATE SET
                school_id = EXCLUDED.school_id,
                name = EXCLUDED.name,
                provider_name = EXCLUDED.provider_name,
                connection_type = EXCLUDED.connection_type,
                contracted_download_mbps = EXCLUDED.contracted_download_mbps,
                contracted_upload_mbps = EXCLUDED.contracted_upload_mbps,
                status = EXCLUDED.status,
                updated_at_utc = now();
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.AddWithValue(request.LineName.Trim());
        AddNullableText(command, request.ProviderName);
        AddNullableText(command, request.ConnectionType);
        AddNullableDecimal(command, request.ContractedDownloadMbps);
        AddNullableDecimal(command, request.ContractedUploadMbps);
        command.Parameters.AddWithValue(request.LineStatus);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceRegistrationRequest request,
        Guid schoolId,
        Guid lineId,
        Guid deviceId,
        string deviceIdentifier,
        byte[] tokenHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO devices (
                id, school_id, line_id, device_identifier, name, room,
                connection_type, token_hash, is_blocked)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, false)
            ON CONFLICT (id) DO UPDATE SET
                school_id = EXCLUDED.school_id,
                line_id = EXCLUDED.line_id,
                device_identifier = EXCLUDED.device_identifier,
                name = EXCLUDED.name,
                room = EXCLUDED.room,
                connection_type = EXCLUDED.connection_type,
                token_hash = EXCLUDED.token_hash,
                is_blocked = false;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(deviceIdentifier);
        command.Parameters.AddWithValue(request.DeviceName.Trim());
        AddNullableText(command, request.Room);
        AddNullableText(command, request.ConnectionType);
        command.Parameters.AddWithValue(tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });

    private static void AddNullableDecimal(NpgsqlCommand command, decimal? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Numeric,
            Value = value is null ? DBNull.Value : value.Value
        });
}
