using Microsoft.AspNetCore.Identity;
using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Security;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresUserRepository(NpgsqlDataSource dataSource, TimeProvider clock)
{
    private readonly PasswordHasher<string> hasher = new();
    private const string Columns = "id, login, display_name, role, school_id, district_city, provider_name, is_blocked";
    private static readonly PasswordHasher<string> DummyHasher = new();
    private static readonly string DummyHash = DummyHasher.HashPassword("dummy", "Unused timing equalization password");

    public async Task<UserOverview?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (token.Length is < 20 or > 256) return null;
        await using var command = dataSource.CreateCommand($"SELECT {string.Join(",", Columns.Split(',').Select(c => "u." + c.Trim()))} FROM monitoring_users u JOIN user_sessions s ON s.user_id=u.id WHERE s.token_hash=$1 AND s.expires_at_utc>$2 AND s.security_version=u.security_version AND NOT u.is_blocked");
        command.Parameters.AddWithValue(DeviceTokenHasher.Hash(token));
        command.Parameters.AddWithValue(clock.GetUtcNow());
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, int sessionMinutes, CancellationToken ct)
    {
        if (request.Login is null || request.Login.Length > 64 || request.Password is null || request.Password.Length > 128) return null;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        UserOverview? user = null; string? hash = null; int version = 0, failures = 0; DateTimeOffset? locked = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"SELECT {Columns}, password_hash, security_version, failed_login_count, locked_until_utc FROM monitoring_users WHERE login=$1 FOR UPDATE";
            command.Parameters.AddWithValue(request.Login.Trim().ToLowerInvariant());
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                user = ReadUser(reader); hash = reader.GetString(8); version = reader.GetInt32(9); failures = reader.GetInt32(10);
                locked = reader.IsDBNull(11) ? null : new DateTimeOffset(reader.GetDateTime(11));
            }
        }
        var now = clock.GetUtcNow();
        var verified = hasher.VerifyHashedPassword(request.Login, hash ?? DummyHash, request.Password);
        if (user is null || user.IsBlocked || locked > now || verified == PasswordVerificationResult.Failed)
        {
            if (user is not null && !user.IsBlocked && !(locked > now))
            {
                failures = locked is not null ? 1 : failures + 1;
                await using var fail = connection.CreateCommand(); fail.Transaction = transaction;
                fail.CommandText = "UPDATE monitoring_users SET failed_login_count=$2, locked_until_utc=$3 WHERE id=$1";
                fail.Parameters.AddWithValue(user.UserId); fail.Parameters.AddWithValue(failures);
                AddNullable(fail, NpgsqlDbType.TimestampTz, failures >= 5 ? now.AddMinutes(15) : null);
                await fail.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct); return null;
        }
        await using (var success = connection.CreateCommand())
        {
            success.Transaction = transaction;
            success.CommandText = "UPDATE monitoring_users SET failed_login_count=0, locked_until_utc=NULL, password_hash=$2 WHERE id=$1";
            success.Parameters.AddWithValue(user.UserId);
            success.Parameters.AddWithValue(verified == PasswordVerificationResult.SuccessRehashNeeded ? hasher.HashPassword(user.Login, request.Password) : hash!);
            await success.ExecuteNonQueryAsync(ct);
        }
        var token = DeviceTokenIssuer.Generate(); var expires = now.AddMinutes(sessionMinutes);
        await using (var session = connection.CreateCommand())
        {
            session.Transaction = transaction;
            session.CommandText = "INSERT INTO user_sessions(token_hash,user_id,security_version,expires_at_utc) VALUES($1,$2,$3,$4)";
            session.Parameters.AddWithValue(DeviceTokenHasher.Hash(token)); session.Parameters.AddWithValue(user.UserId);
            session.Parameters.AddWithValue(version); session.Parameters.AddWithValue(expires);
            await session.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new LoginResponse(token, expires, user);
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM user_sessions WHERE token_hash=$1");
        command.Parameters.AddWithValue(DeviceTokenHasher.Hash(token)); await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid[]?> GetAllowedLinesAsync(UserOverview user, CancellationToken ct)
    {
        if (user.Role is UserRole.Administrator or UserRole.Regional) return null;
        await using var command = dataSource.CreateCommand("""
            SELECT l.id FROM internet_lines l JOIN schools s ON s.id=l.school_id
            WHERE ($1='School' AND s.id=$2) OR ($1='District' AND s.district_city=$3)
               OR ($1='Provider' AND l.provider_name=$4)
            """);
        command.Parameters.AddWithValue(user.Role.ToString());
        AddNullable(command, NpgsqlDbType.Uuid, user.SchoolId);
        AddNullable(command, NpgsqlDbType.Text, user.DistrictCity);
        AddNullable(command, NpgsqlDbType.Text, user.ProviderName);
        var result = new List<Guid>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0)); return result.ToArray();
    }

    public async Task<IReadOnlyList<UserOverview>> ListAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM monitoring_users ORDER BY login");
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<UserOverview>();
        while (await reader.ReadAsync(ct)) result.Add(ReadUser(reader)); return result;
    }

    public async Task<UserOverview> CreateAsync(UserCreateRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand("""
            INSERT INTO monitoring_users(id,login,display_name,password_hash,role,school_id,district_city,provider_name)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8)
            """);
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(request.Login.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue(request.DisplayName.Trim()); command.Parameters.AddWithValue(hasher.HashPassword(request.Login, request.Password));
        AddScope(command, request.Role, request.SchoolId, request.DistrictCity, request.ProviderName);
        await command.ExecuteNonQueryAsync(ct);
        return new UserOverview(id, request.Login.Trim().ToLowerInvariant(), request.DisplayName.Trim(), request.Role,
            request.Role == UserRole.School ? request.SchoolId : null,
            request.Role == UserRole.District ? request.DistrictCity?.Trim() : null,
            request.Role == UserRole.Provider ? request.ProviderName?.Trim() : null, false);
    }

    // Serializes administrator changes so simultaneous requests cannot remove the last active administrator.
    public async Task<string> UpdateAsync(Guid id, UserUpdateRequest request, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(74932618)"; await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "SELECT role, is_blocked FROM monitoring_users WHERE id=$1 FOR UPDATE"; command.Parameters.AddWithValue(id);
        bool wasAdmin;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return "NotFound";
            wasAdmin = reader.GetString(0) == "Administrator" && !reader.GetBoolean(1);
        }
        if (wasAdmin && (request.Role != UserRole.Administrator || request.IsBlocked))
        {
            command.Parameters.Clear(); command.CommandText = "SELECT count(*) FROM monitoring_users WHERE role='Administrator' AND NOT is_blocked";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) <= 1) return "LastAdministrator";
        }
        command.Parameters.Clear();
        command.CommandText = """
            UPDATE monitoring_users SET display_name=$2,role=$3,school_id=$4,district_city=$5,provider_name=$6,
                is_blocked=$7,security_version=security_version+1,updated_at_utc=now() WHERE id=$1
            """;
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(request.DisplayName.Trim());
        AddScope(command, request.Role, request.SchoolId, request.DistrictCity, request.ProviderName);
        command.Parameters.AddWithValue(request.IsBlocked); await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct); return "Updated";
    }

    public async Task<bool> SetPasswordAsync(Guid id, string password, string? currentPassword, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        string login, oldHash;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction; read.CommandText = "SELECT login,password_hash FROM monitoring_users WHERE id=$1 FOR UPDATE";
            read.Parameters.AddWithValue(id); await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return false; login = reader.GetString(0); oldHash = reader.GetString(1);
        }
        if (currentPassword is not null && hasher.VerifyHashedPassword(login, oldHash, currentPassword) == PasswordVerificationResult.Failed) return false;
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE monitoring_users SET password_hash=$2,security_version=security_version+1,failed_login_count=0,locked_until_utc=NULL,updated_at_utc=now() WHERE id=$1";
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(hasher.HashPassword(login, password));
        await command.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); return true;
    }

    public async Task<long> BeginAuditAsync(Guid? userId, string actor, string action, string path, string? clientIp, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO audit_events(user_id,actor,action,path,client_ip) VALUES($1,$2,$3,$4,$5) RETURNING id");
        AddNullable(command, NpgsqlDbType.Uuid, userId); command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(action); command.Parameters.AddWithValue(path);
        AddNullable(command, NpgsqlDbType.Text, clientIp);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task CompleteAuditAsync(long id, int statusCode, CancellationToken ct, UserOverview? user = null)
    {
        await using var command = dataSource.CreateCommand("UPDATE audit_events SET completed_at_utc=now(),status_code=$2,user_id=coalesce($3,user_id),actor=coalesce($4,actor) WHERE id=$1");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(statusCode);
        AddNullable(command,NpgsqlDbType.Uuid,user?.UserId); AddNullable(command,NpgsqlDbType.Text,user?.Login);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetAuditAsync(DateTimeOffset from, DateTimeOffset to, long? beforeId, int limit, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT id,started_at_utc,completed_at_utc,user_id,actor,action,path,status_code,client_ip FROM audit_events WHERE started_at_utc>=$1 AND started_at_utc<$2 AND ($3::bigint IS NULL OR id<$3) ORDER BY id DESC LIMIT $4");
        command.Parameters.AddWithValue(from); command.Parameters.AddWithValue(to); AddNullable(command, NpgsqlDbType.Bigint, beforeId); command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<AuditEntry>();
        while (await reader.ReadAsync(ct)) result.Add(new AuditEntry(reader.GetInt64(0),new DateTimeOffset(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : new DateTimeOffset(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.IsDBNull(7) ? null : reader.GetInt32(7),Text(reader,8)));
        return result;
    }

    public async Task<Guid?> FindResourceLineAsync(string table, Guid id, CancellationToken ct)
    {
        if (table is not ("devices" or "incidents")) throw new ArgumentException("Unsupported resource", nameof(table));
        await using var command = dataSource.CreateCommand($"SELECT line_id FROM {table} WHERE id=$1");
        command.Parameters.AddWithValue(id); return await command.ExecuteScalarAsync(ct) is Guid line ? line : null;
    }

    private static UserOverview ReadUser(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
        Enum.Parse<UserRole>(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetGuid(4),Text(reader,5),Text(reader,6),reader.GetBoolean(7));
    private static string? Text(NpgsqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
    private static void AddNullable(NpgsqlCommand command, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
    private static void AddScope(NpgsqlCommand command, UserRole role, Guid? schoolId, string? district, string? provider)
    {
        command.Parameters.AddWithValue(role.ToString()); AddNullable(command,NpgsqlDbType.Uuid,role == UserRole.School ? schoolId : null);
        AddNullable(command,NpgsqlDbType.Text,role == UserRole.District ? district?.Trim() : null);
        AddNullable(command,NpgsqlDbType.Text,role == UserRole.Provider ? provider?.Trim() : null);
    }
}
