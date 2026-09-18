using Npgsql;

namespace VkoMonitoring.Api.Configuration;

public static class PostgresConnectionStringResolver
{
    private const int DefaultPostgresPort = 5432;

    public static string? Resolve(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        var trimmedValue = configuredValue.Trim();
        if (!trimmedValue.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !trimmedValue.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedValue;
        }

        if (!Uri.TryCreate(trimmedValue, UriKind.Absolute, out var databaseUri) ||
            string.IsNullOrWhiteSpace(databaseUri.Host))
        {
            throw new InvalidOperationException("The PostgreSQL connection URL is invalid.");
        }

        var credentials = databaseUri.UserInfo.Split(':', 2);
        if (credentials.Length != 2 || string.IsNullOrWhiteSpace(credentials[0]))
        {
            throw new InvalidOperationException("The PostgreSQL connection URL must contain a username and password.");
        }

        var databaseName = Uri.UnescapeDataString(databaseUri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The PostgreSQL connection URL must contain a database name.");
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.IsDefaultPort ? DefaultPostgresPort : databaseUri.Port,
            Database = databaseName,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = Uri.UnescapeDataString(credentials[1]),
            ApplicationName = "VkoMonitoring.Api"
        }.ConnectionString;
    }
}
