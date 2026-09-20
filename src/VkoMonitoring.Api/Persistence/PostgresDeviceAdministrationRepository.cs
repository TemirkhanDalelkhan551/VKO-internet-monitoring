using Npgsql;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDeviceAdministrationRepository(NpgsqlDataSource dataSource)
    : IDeviceAdministrationRepository
{
    public Task<bool> SetBlockedAsync(Guid deviceId, bool isBlocked, CancellationToken cancellationToken) =>
        ExecuteActiveUpdateAsync(
            "UPDATE devices SET is_blocked = $2 WHERE id = $1 AND lifecycle_status = 'Active';",
            deviceId, isBlocked, cancellationToken);

    public Task<bool> ReplaceTokenHashAsync(Guid deviceId, byte[] tokenHash, CancellationToken cancellationToken) =>
        ExecuteActiveUpdateAsync(
            "UPDATE devices SET token_hash = $2 WHERE id = $1 AND lifecycle_status = 'Active';",
            deviceId, tokenHash, cancellationToken);

    public async Task<DeviceLifecycleResult?> RebindAsync(
        Guid deviceId, Guid schoolId, Guid lineId, string reason, string actor,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await LockDeviceAsync(connection, transaction, deviceId, cancellationToken);
        if (current is null || current.Value.Status != "Active" ||
            !await LineBelongsToSchoolAsync(connection, transaction, schoolId, lineId, cancellationToken))
            return null;

        await ExecuteAsync(connection, transaction,
            "UPDATE devices SET school_id=$2, line_id=$3 WHERE id=$1;",
            cancellationToken, deviceId, schoolId, lineId);
        await InsertHistoryAsync(connection, transaction, deviceId, "Rebound",
            current.Value.SchoolId, current.Value.LineId, schoolId, lineId, null,
            reason, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(deviceId, "Active", schoolId, lineId, null, null, null);
    }

    public async Task<DeviceLifecycleResult?> ReplaceAsync(
        Guid deviceId, Guid replacementDeviceId, string reason, string actor,
        CancellationToken cancellationToken)
    {
        if (deviceId == replacementDeviceId) return null;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await LockDeviceAsync(connection, transaction, deviceId, cancellationToken);
        var replacement = await LockDeviceAsync(connection, transaction, replacementDeviceId, cancellationToken);
        if (current is null || replacement is null || current.Value.Status != "Active" ||
            replacement.Value.Status != "Active" || current.Value.SchoolId != replacement.Value.SchoolId)
            return null;

        await ExecuteAsync(connection, transaction, """
            UPDATE devices
            SET lifecycle_status='Replaced', is_blocked=true, token_hash=NULL,
                replaced_by_device_id=$2, retired_at_utc=now(), retirement_reason=$3
            WHERE id=$1;
            """, cancellationToken, deviceId, replacementDeviceId, reason.Trim());
        await InsertHistoryAsync(connection, transaction, deviceId, "Replaced",
            current.Value.SchoolId, current.Value.LineId, current.Value.SchoolId,
            current.Value.LineId, replacementDeviceId, reason, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetLifecycleAsync(deviceId, cancellationToken);
    }

    public async Task<DeviceLifecycleResult?> DecommissionAsync(
        Guid deviceId, string reason, string actor, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await LockDeviceAsync(connection, transaction, deviceId, cancellationToken);
        if (current is null || current.Value.Status != "Active") return null;
        await ExecuteAsync(connection, transaction, """
            UPDATE devices
            SET lifecycle_status='Decommissioned', is_blocked=true, token_hash=NULL,
                retired_at_utc=now(), retirement_reason=$2
            WHERE id=$1;
            """, cancellationToken, deviceId, reason.Trim());
        await InsertHistoryAsync(connection, transaction, deviceId, "Decommissioned",
            current.Value.SchoolId, current.Value.LineId, current.Value.SchoolId,
            current.Value.LineId, null, reason, actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetLifecycleAsync(deviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceLifecycleEvent>> GetHistoryAsync(
        Guid deviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, device_id, action, previous_school_id, previous_line_id,
                   current_school_id, current_line_id, replacement_device_id,
                   reason, actor, occurred_at_utc
            FROM device_lifecycle_history WHERE device_id=$1
            ORDER BY occurred_at_utc DESC, id DESC;
            """;
        var result = new List<DeviceLifecycleEvent>();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2),
                GetGuid(reader, 3), GetGuid(reader, 4), GetGuid(reader, 5), GetGuid(reader, 6),
                GetGuid(reader, 7), reader.GetString(8), reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10)));
        return result;
    }

    private async Task<DeviceLifecycleResult?> GetLifecycleAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, lifecycle_status, school_id, line_id, replaced_by_device_id,
                   retired_at_utc, retirement_reason FROM devices WHERE id=$1;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetGuid(3),
            GetGuid(reader, 4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static async Task<(Guid SchoolId, Guid LineId, string Status)?> LockDeviceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT school_id, line_id, lifecycle_status FROM devices WHERE id=$1 FOR UPDATE;";
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2))
            : null;
    }

    private static async Task<bool> LineBelongsToSchoolAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid schoolId, Guid lineId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM internet_lines WHERE id=$1 AND school_id=$2);";
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(schoolId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId, string action,
        Guid previousSchoolId, Guid previousLineId, Guid currentSchoolId, Guid currentLineId,
        Guid? replacementDeviceId, string reason, string actor, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO device_lifecycle_history (
                device_id, action, previous_school_id, previous_line_id,
                current_school_id, current_line_id, replacement_device_id, reason, actor)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9);
            """;
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(action);
        command.Parameters.AddWithValue(previousSchoolId);
        command.Parameters.AddWithValue(previousLineId);
        command.Parameters.AddWithValue(currentSchoolId);
        command.Parameters.AddWithValue(currentLineId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid,
            Value = replacementDeviceId is null ? DBNull.Value : replacementDeviceId.Value
        });
        command.Parameters.AddWithValue(reason.Trim());
        command.Parameters.AddWithValue(actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> ExecuteActiveUpdateAsync<T>(
        string sql, Guid deviceId, T value, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(value!);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        CancellationToken cancellationToken, params object?[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue(values[index] ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid? GetGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}
