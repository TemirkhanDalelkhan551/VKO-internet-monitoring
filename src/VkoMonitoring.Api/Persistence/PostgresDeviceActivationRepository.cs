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
        string? deviceIdentifier,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.school_id, s.name, c.line_id, l.name,
                   l.provider_name, l.connection_type, c.expires_at_utc,
                   c.used_at_utc IS NOT NULL,
                   c.used_at_utc IS NULL AND EXISTS (
                       SELECT 1 FROM devices active_device
                       WHERE active_device.device_identifier = $2
                         AND active_device.lifecycle_status = 'Active')
            FROM device_activation_codes c
            JOIN schools s ON s.id = c.school_id
            JOIN internet_lines l ON l.id = c.line_id AND l.school_id = c.school_id
            LEFT JOIN devices d ON d.id = c.used_by_device_id
            WHERE c.code_hash = $1
              AND c.revoked_at_utc IS NULL
              AND (
                    (c.used_at_utc IS NULL AND c.expires_at_utc > now())
                    OR
                    (c.used_at_utc >= now() - interval '24 hours'
                     AND d.device_identifier = $2
                     AND d.lifecycle_status = 'Active')
                  );
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(codeHash);
        AddNullableText(command, deviceIdentifier);
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
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetBoolean(7),
            reader.GetBoolean(8));
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
        var codeBinding = await LockCodeForActivationAsync(
            connection, transaction, codeHash, deviceIdentifier, cancellationToken);
        if (codeBinding is null)
        {
            return null;
        }

        var recovered = codeBinding.Value.UsedByDeviceId is not null;
        var existingDevice = recovered
            ? null
            : await LockActiveDeviceByIdentifierAsync(
                connection, transaction, deviceIdentifier, cancellationToken);
        var effectiveDeviceId = codeBinding.Value.UsedByDeviceId ?? existingDevice?.DeviceId ?? deviceId;
        var reconfigured = existingDevice is not null;
        if (recovered)
        {
            await RecoverDeviceAsync(
                connection, transaction, request, effectiveDeviceId,
                deviceTokenHash, cancellationToken);
        }
        else if (existingDevice is not null)
        {
            await ReconfigureDeviceAsync(
                connection, transaction, request, effectiveDeviceId,
                codeBinding.Value.SchoolId, codeBinding.Value.LineId,
                existingDevice.Value, deviceTokenHash, cancellationToken);
            await MarkCodeUsedAsync(
                connection, transaction, codeBinding.Value.CodeId,
                effectiveDeviceId, cancellationToken);
        }
        else
        {
            await InsertDeviceAsync(
                connection, transaction, request, codeBinding.Value.SchoolId,
                codeBinding.Value.LineId, effectiveDeviceId, deviceIdentifier,
                deviceTokenHash, cancellationToken);
            await MarkCodeUsedAsync(
                connection, transaction, codeBinding.Value.CodeId,
                effectiveDeviceId, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return new ActivatedDeviceBinding(
            codeBinding.Value.SchoolId,
            codeBinding.Value.LineId,
            effectiveDeviceId,
            deviceIdentifier,
            recovered,
            reconfigured);
    }

    private static async Task<(Guid CodeId, Guid SchoolId, Guid LineId, Guid? UsedByDeviceId)?> LockCodeForActivationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        byte[] codeHash,
        string deviceIdentifier,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.id, c.school_id, c.line_id, c.used_by_device_id
            FROM device_activation_codes c
            LEFT JOIN devices d ON d.id = c.used_by_device_id
            WHERE c.code_hash = $1
              AND c.revoked_at_utc IS NULL
              AND (
                    (c.used_at_utc IS NULL AND c.expires_at_utc > now())
                    OR
                    (c.used_at_utc >= now() - interval '24 hours'
                     AND d.device_identifier = $2
                     AND d.lifecycle_status = 'Active')
                  )
            FOR UPDATE OF c;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(codeHash);
        command.Parameters.AddWithValue(deviceIdentifier);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return (
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
    }

    private static async Task RecoverDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceActivationRequest request,
        Guid deviceId,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE devices
            SET name = $2, room = $3, connection_type = $4,
                token_hash = $5, is_blocked = false
            WHERE id = $1 AND lifecycle_status = 'Active';
            """;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(request.DeviceName.Trim());
        AddNullableText(command, request.Room);
        AddNullableText(command, request.ConnectionType);
        command.Parameters.AddWithValue(deviceTokenHash);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The device cannot be recovered.");
        }
    }

    private static async Task<(Guid DeviceId, Guid SchoolId, Guid LineId)?> LockActiveDeviceByIdentifierAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string deviceIdentifier,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, school_id, line_id
            FROM devices
            WHERE device_identifier = $1 AND lifecycle_status = 'Active'
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(deviceIdentifier);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2))
            : null;
    }

    private static async Task ReconfigureDeviceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DeviceActivationRequest request,
        Guid deviceId,
        Guid schoolId,
        Guid lineId,
        (Guid DeviceId, Guid SchoolId, Guid LineId) previousBinding,
        byte[] deviceTokenHash,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE devices
            SET school_id = $2, line_id = $3, name = $4, room = $5,
                connection_type = $6, token_hash = $7, is_blocked = false
            WHERE id = $1 AND lifecycle_status = 'Active';
            """;
        update.Parameters.AddWithValue(deviceId);
        update.Parameters.AddWithValue(schoolId);
        update.Parameters.AddWithValue(lineId);
        update.Parameters.AddWithValue(request.DeviceName.Trim());
        AddNullableText(update, request.Room);
        AddNullableText(update, request.ConnectionType);
        update.Parameters.AddWithValue(deviceTokenHash);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The device cannot be reconfigured.");
        }

        await using var history = connection.CreateCommand();
        history.Transaction = transaction;
        history.CommandText = """
            INSERT INTO device_lifecycle_history (
                device_id, action, previous_school_id, previous_line_id,
                current_school_id, current_line_id, reason, actor)
            VALUES ($1, 'Rebound', $2, $3, $4, $5, $6, 'activation-code');
            """;
        history.Parameters.AddWithValue(deviceId);
        history.Parameters.AddWithValue(previousBinding.SchoolId);
        history.Parameters.AddWithValue(previousBinding.LineId);
        history.Parameters.AddWithValue(schoolId);
        history.Parameters.AddWithValue(lineId);
        history.Parameters.AddWithValue("Повторная активация этого же компьютера по одноразовому коду.");
        await history.ExecuteNonQueryAsync(cancellationToken);
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
