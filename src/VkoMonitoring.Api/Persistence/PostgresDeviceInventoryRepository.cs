using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDeviceInventoryRepository(NpgsqlDataSource dataSource) : IDeviceInventoryRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DeviceInventoryRecord?> GetAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT snapshot, updated_at_utc, changed_at_utc FROM device_inventories WHERE device_id = $1;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var inventory = JsonSerializer.Deserialize<HardwareInventory>(reader.GetString(0), Json);
        return inventory is null ? null : new DeviceInventoryRecord(inventory,
            reader.GetFieldValue<DateTimeOffset>(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    public async Task<DeviceInventorySaveResult> SaveAsync(HardwareInventory inventory, CancellationToken cancellationToken)
    {
        var canonical = JsonSerializer.Serialize(inventory with { CollectedAtUtc = default }, Json);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var snapshot = JsonSerializer.Serialize(inventory, Json);
        const string sql = """
            INSERT INTO device_inventories (device_id, schema_version, fingerprint, snapshot, collected_at_utc, updated_at_utc, changed_at_utc)
            VALUES ($1, $2, $3, $4::jsonb, $5, now(), now())
            ON CONFLICT (device_id) DO UPDATE SET
                schema_version = EXCLUDED.schema_version,
                snapshot = EXCLUDED.snapshot,
                collected_at_utc = EXCLUDED.collected_at_utc,
                updated_at_utc = now(),
                changed_at_utc = CASE WHEN device_inventories.fingerprint <> EXCLUDED.fingerprint THEN now() ELSE device_inventories.changed_at_utc END,
                fingerprint = EXCLUDED.fingerprint
            RETURNING (xmax = 0 OR changed_at_utc = updated_at_utc), updated_at_utc;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(inventory.DeviceId);
        command.Parameters.AddWithValue(inventory.SchemaVersion);
        command.Parameters.AddWithValue(fingerprint);
        command.Parameters.AddWithValue(snapshot);
        command.Parameters.AddWithValue(inventory.CollectedAtUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var changed = reader.GetBoolean(0);
        var updated = reader.GetFieldValue<DateTimeOffset>(1);

        if (changed)
        {
            const string historySql = """
                INSERT INTO device_inventory_changes (device_id, occurred_at_utc, fingerprint, summary)
                VALUES ($1, $2, $3, 'Конфигурация оборудования обновлена');
                """;
            await using var history = dataSource.CreateCommand(historySql);
            history.Parameters.AddWithValue(inventory.DeviceId);
            history.Parameters.AddWithValue(updated);
            history.Parameters.AddWithValue(fingerprint);
            await history.ExecuteNonQueryAsync(cancellationToken);
        }

        return new DeviceInventorySaveResult(changed, updated);
    }
}
