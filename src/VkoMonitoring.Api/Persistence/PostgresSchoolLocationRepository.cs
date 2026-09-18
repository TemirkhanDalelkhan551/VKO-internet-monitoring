using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresSchoolLocationRepository(NpgsqlDataSource dataSource)
{
    public async Task<bool> UpdateAsync(Guid schoolId, SchoolLocationRequest location, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE schools SET latitude=$2, longitude=$3, updated_at_utc=now() WHERE id=$1;
            """);
        command.Parameters.AddWithValue(schoolId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Double, Value = (object?)location.Latitude ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Double, Value = (object?)location.Longitude ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
