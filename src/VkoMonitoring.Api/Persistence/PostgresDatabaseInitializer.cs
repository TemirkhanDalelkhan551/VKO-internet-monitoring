using Npgsql;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Security;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDatabaseInitializer(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options)
{
    private const string SchemaResourceName =
        "VkoMonitoring.Api.Persistence.Sql.001_initial_schema.sql";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(PostgresDatabaseInitializer).Assembly
            .GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException($"Embedded schema {SchemaResourceName} was not found.");
        using var reader = new StreamReader(stream);
        var schemaSql = await reader.ReadToEndAsync(cancellationToken);

        await using var schemaCommand = dataSource.CreateCommand(schemaSql);
        await schemaCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var usersStream = typeof(PostgresDatabaseInitializer).Assembly
            .GetManifestResourceStream("VkoMonitoring.Api.Persistence.Sql.002_users_and_audit.sql")
            ?? throw new InvalidOperationException("User schema resource was not found.");
        using var usersReader = new StreamReader(usersStream);
        await using var usersCommand = dataSource.CreateCommand(await usersReader.ReadToEndAsync(cancellationToken));
        await usersCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var (deviceIdentifier, binding) in options.DeviceBindings)
        {
            if (!Guid.TryParse(deviceIdentifier, out var deviceId))
            {
                throw new InvalidOperationException($"Device binding key {deviceIdentifier} is not a valid UUID.");
            }

            options.DeviceTokens.TryGetValue(deviceIdentifier, out var deviceToken);
            await SeedDeviceAsync(deviceId, binding, deviceToken, cancellationToken);
        }
    }

    private async Task SeedDeviceAsync(
        Guid deviceId,
        DeviceBindingOptions binding,
        string? deviceToken,
        CancellationToken cancellationToken)
    {
        const string schoolSql = """
            INSERT INTO schools (id, name)
            VALUES ($1, $2)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                updated_at_utc = now();
            """;

        const string lineSql = """
            INSERT INTO internet_lines (id, school_id, name, status)
            VALUES ($1, $2, $3, 'Primary')
            ON CONFLICT (id) DO UPDATE SET
                school_id = EXCLUDED.school_id,
                name = EXCLUDED.name,
                updated_at_utc = now();
            """;

        const string deviceSql = """
            INSERT INTO devices (id, school_id, line_id, device_identifier, name, token_hash)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (id) DO UPDATE SET
                school_id = EXCLUDED.school_id,
                line_id = EXCLUDED.line_id,
                device_identifier = EXCLUDED.device_identifier,
                name = EXCLUDED.name,
                token_hash = COALESCE(EXCLUDED.token_hash, devices.token_hash);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var schoolCommand = connection.CreateCommand())
        {
            schoolCommand.Transaction = transaction;
            schoolCommand.CommandText = schoolSql;
            schoolCommand.Parameters.AddWithValue(binding.SchoolId);
            schoolCommand.Parameters.AddWithValue(
                RequiredOrFallback(binding.SchoolName, binding.SchoolId.ToString("D")));
            await schoolCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var lineCommand = connection.CreateCommand())
        {
            lineCommand.Transaction = transaction;
            lineCommand.CommandText = lineSql;
            lineCommand.Parameters.AddWithValue(binding.LineId);
            lineCommand.Parameters.AddWithValue(binding.SchoolId);
            lineCommand.Parameters.AddWithValue(
                RequiredOrFallback(binding.LineName, binding.LineId.ToString("D")));
            await lineCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deviceCommand = connection.CreateCommand())
        {
            deviceCommand.Transaction = transaction;
            deviceCommand.CommandText = deviceSql;
            deviceCommand.Parameters.AddWithValue(deviceId);
            deviceCommand.Parameters.AddWithValue(binding.SchoolId);
            deviceCommand.Parameters.AddWithValue(binding.LineId);
            deviceCommand.Parameters.AddWithValue(deviceId.ToString("D"));
            deviceCommand.Parameters.AddWithValue(
                RequiredOrFallback(binding.DeviceName, deviceId.ToString("D")));
            deviceCommand.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bytea,
                Value = ConfigurationSecret.IsConfigured(deviceToken)
                    ? DeviceTokenHasher.Hash(deviceToken!)
                    : DBNull.Value
            });
            await deviceCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string RequiredOrFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
