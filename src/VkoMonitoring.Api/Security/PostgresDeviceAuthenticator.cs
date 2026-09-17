using Npgsql;

namespace VkoMonitoring.Api.Security;

public sealed class PostgresDeviceAuthenticator(NpgsqlDataSource dataSource)
    : IDeviceAuthenticator
{
    public async Task<bool> IsAuthorizedAsync(
        Guid schoolId,
        Guid deviceId,
        Guid lineId,
        string? suppliedToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM devices
                WHERE id = $1
                  AND school_id = $2
                  AND line_id = $3
                  AND token_hash = $4
                  AND is_blocked = false);
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(DeviceTokenHasher.Hash(suppliedToken));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
}
