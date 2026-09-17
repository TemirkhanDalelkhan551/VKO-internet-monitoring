using Npgsql;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDevicePresenceRepository(NpgsqlDataSource dataSource)
    : IDevicePresenceRepository
{
    public async Task<bool> RecordHeartbeatAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE devices
            SET last_seen_at_utc = $4,
                agent_version = $5
            WHERE id = $1
              AND school_id = $2
              AND line_id = $3
              AND is_blocked = false;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(heartbeat.DeviceId);
        command.Parameters.AddWithValue(heartbeat.SchoolId);
        command.Parameters.AddWithValue(heartbeat.LineId);
        command.Parameters.AddWithValue(heartbeat.SentAtUtc);
        command.Parameters.AddWithValue(heartbeat.AgentVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
