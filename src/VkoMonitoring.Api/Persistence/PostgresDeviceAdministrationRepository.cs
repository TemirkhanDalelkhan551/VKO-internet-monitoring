using Npgsql;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDeviceAdministrationRepository(NpgsqlDataSource dataSource)
    : IDeviceAdministrationRepository
{
    public Task<bool> SetBlockedAsync(
        Guid deviceId,
        bool isBlocked,
        CancellationToken cancellationToken) =>
        ExecuteUpdateAsync(
            "UPDATE devices SET is_blocked = $2 WHERE id = $1;",
            deviceId,
            isBlocked,
            cancellationToken);

    public Task<bool> ReplaceTokenHashAsync(
        Guid deviceId,
        byte[] tokenHash,
        CancellationToken cancellationToken) =>
        ExecuteUpdateAsync(
            "UPDATE devices SET token_hash = $2 WHERE id = $1;",
            deviceId,
            tokenHash,
            cancellationToken);

    private async Task<bool> ExecuteUpdateAsync<T>(
        string sql,
        Guid deviceId,
        T value,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(value!);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }
}
