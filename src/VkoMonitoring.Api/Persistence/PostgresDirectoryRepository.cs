using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresDirectoryRepository(NpgsqlDataSource dataSource)
{
    public async Task<bool> SaveSchoolAsync(Guid id, SchoolSaveRequest request, bool create, CancellationToken cancellationToken)
    {
        var sql = create ? """
            INSERT INTO schools(id,name,district_city,address,responsible_name,responsible_position,responsible_phone,responsible_email)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8);
            """ : """
            UPDATE schools SET name=$2,district_city=$3,address=$4,responsible_name=$5,
                responsible_position=$6,responsible_phone=$7,responsible_email=$8,updated_at_utc=now() WHERE id=$1;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(request.Name.Trim());
        foreach (var value in new[] { request.DistrictCity, request.Address, request.ResponsibleName, request.ResponsiblePosition, request.ResponsiblePhone, request.ResponsibleEmail })
            Add(command, NpgsqlDbType.Text, Clean(value));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> SaveLineAsync(Guid schoolId, Guid lineId, LineSaveRequest request, bool create, CancellationToken cancellationToken)
    {
        var sql = create ? """
            INSERT INTO internet_lines(school_id,id,name,provider_name,connection_type,contracted_download_mbps,
                contracted_upload_mbps,contract_number,contract_date,status)
            SELECT $1,$2,$3,$4,$5,$6,$7,$8,$9,$10 FROM schools WHERE id=$1;
            """ : """
            UPDATE internet_lines SET name=$3,provider_name=$4,connection_type=$5,contracted_download_mbps=$6,
                contracted_upload_mbps=$7,contract_number=$8,contract_date=$9,status=$10,updated_at_utc=now()
            WHERE school_id=$1 AND id=$2;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(schoolId); command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(request.Name.Trim());
        Add(command, NpgsqlDbType.Text, Clean(request.ProviderName));
        Add(command, NpgsqlDbType.Text, Clean(request.ConnectionType));
        Add(command, NpgsqlDbType.Numeric, request.ContractedDownloadMbps);
        Add(command, NpgsqlDbType.Numeric, request.ContractedUploadMbps);
        Add(command, NpgsqlDbType.Text, Clean(request.ContractNumber));
        Add(command, NpgsqlDbType.Date, request.ContractDate);
        command.Parameters.AddWithValue(request.LineStatus);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Add(NpgsqlCommand command, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
}
