using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDeviceActivationRepository(NpgsqlDataSource dataSource)
    : IDeviceActivationRepository
{
    public async Task<bool> CreateCodeAsync(
        Guid activationCodeId,
        Guid schoolId,
        Guid lineId,
        byte[] codeHash,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO device_activation_codes (
                id, code_hash, school_id, line_id, expires_at_utc)
            SELECT $1, $2, $3, $4, $5
            FROM internet_lines
            WHERE id = $4 AND school_id = $3;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(activationCodeId);
        command.Parameters.AddWithValue(codeHash);
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(expiresAtUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<ActivationCodeOverview>> ListCodesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id, c.school_id, s.name, c.line_id, l.name,
                   c.created_at_utc, c.expires_at_utc, c.used_at_utc,
                   c.used_by_device_id, c.revoked_at_utc,
                   CASE
                     WHEN c.revoked_at_utc IS NOT NULL THEN 'Revoked'
                     WHEN c.used_at_utc IS NOT NULL THEN 'Used'
                     WHEN c.expires_at_utc <= now() THEN 'Expired'
                     ELSE 'Active'
                   END
            FROM device_activation_codes c
            JOIN schools s ON s.id = c.school_id
            JOIN internet_lines l ON l.id = c.line_id AND l.school_id = c.school_id
            ORDER BY c.created_at_utc DESC
            LIMIT $1;
            """;
        var result = new List<ActivationCodeOverview>();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ActivationCodeOverview(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.GetString(10)));
        }
        return result;
    }

    public async Task<bool> RevokeCodeAsync(Guid activationCodeId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE device_activation_codes
            SET revoked_at_utc = now()
            WHERE id = $1 AND used_at_utc IS NULL AND revoked_at_utc IS NULL AND expires_at_utc > now();
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(activationCodeId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<ActivationCodePreviewResult?> PreviewAsync(
        byte[] codeHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.school_id, s.name, c.line_id, l.name,
                   l.provider_name, l.connection_type, c.expires_at_utc
            FROM device_activation_codes c
            JOIN schools s ON s.id = c.school_id
            JOIN internet_lines l ON l.id = c.line_id AND l.school_id = c.school_id
            WHERE c.code_hash = $1
              AND c.used_at_utc IS NULL
              AND c.revoked_at_utc IS NULL
              AND c.expires_at_utc > now();
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(codeHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActivationCodePreviewResult(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    public async Task<ActivatedDeviceBinding?> ActivateAsync(
        DeviceActivationRequest request,
        Guid deviceId,
        string deviceIdentifier,
        byte[] codeHash,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var codeBinding = await LockValidCodeAsync(connection, transaction, codeHash, cancellationToken);
        if (codeBinding is null)
        {
            return null;
        }

        await InsertDeviceAsync(
            connection,
            transaction,
            request,
            codeBinding.Value.SchoolId,
            codeBinding.Value.LineId,
            deviceId,
            deviceIdentifier,
            deviceTokenHash,
            cancellationToken);
        await MarkCodeUsedAsync(
            connection,
            transaction,
            codeBinding.Value.CodeId,
            deviceId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ActivatedDeviceBinding(
            codeBinding.Value.SchoolId,
            codeBinding.Value.LineId,
            deviceId,
            deviceIdentifier);
    }

    private static async Task<(Guid CodeId, Guid SchoolId, Guid LineId)?> LockValidCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        byte[] codeHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, school_id, line_id
            FROM device_activation_codes
            WHERE code_hash = $1
              AND used_at_utc IS NULL
              AND revoked_at_utc IS NULL
              AND expires_at_utc > now()
            FOR UPDATE;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(codeHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    private static async Task InsertDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceActivationRequest request,
        Guid schoolId,
        Guid lineId,
        Guid deviceId,
        string deviceIdentifier,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO devices (
                id, school_id, line_id, device_identifier, name, room,
                connection_type, token_hash, is_blocked)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, false);
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
        command.Parameters.AddWithValue(deviceTokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkCodeUsedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid activationCodeId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE device_activation_codes
            SET used_at_utc = now(), used_by_device_id = $2
            WHERE id = $1;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(activationCodeId);
        command.Parameters.AddWithValue(deviceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddNullableText(NpgsqlCommand command, string? value) =>
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });
}
