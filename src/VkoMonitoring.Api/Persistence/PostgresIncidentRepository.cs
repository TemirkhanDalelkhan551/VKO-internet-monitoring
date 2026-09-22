using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Api.Security;

namespace VkoMonitoring.Api.Persistence;

public sealed class PostgresIncidentRepository(
    NpgsqlDataSource dataSource,
    MonitoringApiOptions options,
    TimeProvider timeProvider,
    MonitoringAccessContext access) : IIncidentRepository
{
    public async Task<IncidentNotificationEvent?> ProcessLatestMeasurementAsync(
        Guid lineId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireLineLockAsync(connection, transaction, lineId, cancellationToken);

        var signals = await GetSignalsAsync(connection, transaction, lineId, cancellationToken);
        if (signals.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var openIncident = await GetOpenIncidentAsync(connection, transaction, lineId, cancellationToken);
        var transition = IncidentDetectionPolicy.DetermineTransition(
            signals.Select(signal => signal.Signal).ToArray(),
            openIncident is not null,
            options.Incidents);

        IncidentNotificationEvent? notification = null;
        if (transition == IncidentTransition.Open)
        {
            notification = await CreateIncidentAsync(connection, transaction, signals, cancellationToken);
        }
        else if (transition == IncidentTransition.Resolve && openIncident is not null)
        {
            notification = await ResolveIncidentAsync(
                connection,
                transaction,
                openIncident,
                signals[0],
                cancellationToken);
        }
        else if (openIncident is not null)
        {
            await UpdateLatestEvidenceAsync(
                connection,
                transaction,
                openIncident.IncidentId,
                signals[0],
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return notification;
    }

    public async Task<IReadOnlyList<IncidentOverview>> GetIncidentsAsync(
        Guid? schoolId,
        IncidentStatus? status,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT i.id,
                   'INC-' || lpad(i.incident_number::text, 6, '0'),
                   i.school_id, s.name, i.line_id, l.name, l.provider_name,
                   i.source, i.problem_type, i.status, i.title, i.description,
                   i.started_at_utc, i.detected_at_utc, i.sent_to_provider_at_utc,
                   i.recovered_at_utc, i.closed_at_utc,
                   greatest(0, floor(extract(epoch FROM
                       (coalesce(i.recovered_at_utc, i.closed_at_utc, now()) - i.started_at_utc))))::bigint,
                   i.latest_measurement_event_id, i.assigned_to
            FROM incidents i
            JOIN schools s ON s.id = i.school_id
            JOIN internet_lines l ON l.id = i.line_id
            WHERE ($1::uuid IS NULL OR i.school_id = $1)
              AND /* access */
              AND ($2::text IS NULL OR i.status = $2)
              AND i.started_at_utc >= $3
              AND i.started_at_utc < $4
            ORDER BY i.started_at_utc DESC
            LIMIT $5;
            """;

        await using var command = dataSource.CreateCommand(sql.Replace("/* access */", access.SqlCondition("i.line_id")));
        AddNullableParameter(command, NpgsqlDbType.Uuid, schoolId);
        AddNullableParameter(command, NpgsqlDbType.Text, status?.ToString());
        command.Parameters.AddWithValue(fromUtc);
        command.Parameters.AddWithValue(toUtc);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var incidents = new List<IncidentOverview>(limit);
        while (await reader.ReadAsync(cancellationToken))
        {
            incidents.Add(ReadIncident(reader));
        }

        return incidents;
    }

    public async Task<IncidentDetails?> GetIncidentAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        const string incidentSql = """
            SELECT i.id,
                   'INC-' || lpad(i.incident_number::text, 6, '0'),
                   i.school_id, s.name, i.line_id, l.name, l.provider_name,
                   i.source, i.problem_type, i.status, i.title, i.description,
                   i.started_at_utc, i.detected_at_utc, i.sent_to_provider_at_utc,
                   i.recovered_at_utc, i.closed_at_utc,
                   greatest(0, floor(extract(epoch FROM
                       (coalesce(i.recovered_at_utc, i.closed_at_utc, now()) - i.started_at_utc))))::bigint,
                   i.latest_measurement_event_id, i.assigned_to
            FROM incidents i
            JOIN schools s ON s.id = i.school_id
            JOIN internet_lines l ON l.id = i.line_id
            WHERE i.id = $1 AND /* access */;
            """;
        const string historySql = """
            SELECT id, occurred_at_utc, action, previous_status, new_status, comment, actor
            FROM incident_history
            WHERE incident_id = $1
            ORDER BY occurred_at_utc, id;
            """;

        IncidentOverview? incident;
        await using (var command = dataSource.CreateCommand(incidentSql.Replace("/* access */", access.SqlCondition("i.line_id"))))
        {
            command.Parameters.AddWithValue(incidentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            incident = await reader.ReadAsync(cancellationToken) ? ReadIncident(reader) : null;
        }

        if (incident is null)
        {
            return null;
        }

        var history = new List<IncidentHistoryEntry>();
        await using (var command = dataSource.CreateCommand(historySql))
        {
            command.Parameters.AddWithValue(incidentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new IncidentHistoryEntry(
                    reader.GetInt64(0),
                    new DateTimeOffset(reader.GetDateTime(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : Enum.Parse<IncidentStatus>(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : Enum.Parse<IncidentStatus>(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        return new IncidentDetails(incident, history);
    }

    public async Task<Guid?> GetOpenIncidentIdAsync(Guid lineId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id
            FROM incidents
            WHERE line_id = $1
              AND status IN ('New', 'SentToProvider', 'InProgress', 'WaitingForInformation')
            ORDER BY started_at_utc DESC
            LIMIT 1;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(lineId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid incidentId ? incidentId : null;
    }

    public async Task<ManualIncidentCreationResult> CreateManualIncidentAsync(
        ManualIncidentCreateRequest request,
        CancellationToken cancellationToken)
    {
        const string bindingSql = """
            SELECT EXISTS (
                SELECT 1
                FROM internet_lines
                WHERE id = $1 AND school_id = $2);
            """;
        const string incidentSql = """
            INSERT INTO incidents (
                id, school_id, line_id, source, problem_type, status, title,
                description, started_at_utc, detected_at_utc, assigned_to)
            VALUES ($1, $2, $3, 'Manual', $4, 'New', $5, $6, $7, $8, $9);
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireLineLockAsync(connection, transaction, request.LineId, cancellationToken);

        await using (var bindingCommand = connection.CreateCommand())
        {
            bindingCommand.Transaction = transaction;
            bindingCommand.CommandText = bindingSql;
            bindingCommand.Parameters.AddWithValue(request.LineId);
            bindingCommand.Parameters.AddWithValue(request.SchoolId);
            var bindingExists = Convert.ToBoolean(
                await bindingCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (!bindingExists)
            {
                return new ManualIncidentCreationResult(
                    ManualIncidentCreationOutcome.BindingNotFound,
                    IncidentId: null);
            }
        }

        if (await GetOpenIncidentAsync(connection, transaction, request.LineId, cancellationToken) is not null)
        {
            return new ManualIncidentCreationResult(
                ManualIncidentCreationOutcome.OpenIncidentExists,
                IncidentId: null);
        }

        var occurredAtUtc = timeProvider.GetUtcNow();
        var startedAtUtc = request.StartedAtUtc ?? occurredAtUtc;
        var incidentId = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = incidentSql;
            command.Parameters.AddWithValue(incidentId);
            command.Parameters.AddWithValue(request.SchoolId);
            command.Parameters.AddWithValue(request.LineId);
            command.Parameters.AddWithValue(request.ProblemType.Trim());
            command.Parameters.AddWithValue(request.Title.Trim());
            command.Parameters.AddWithValue(request.Description.Trim());
            command.Parameters.AddWithValue(startedAtUtc);
            command.Parameters.AddWithValue(occurredAtUtc);
            AddNullableParameter(command, NpgsqlDbType.Text, NormalizeOptional(request.AssignedTo));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await AddHistoryAsync(
            connection,
            transaction,
            incidentId,
            occurredAtUtc,
            "CreatedManually",
            previousStatus: null,
            IncidentStatus.New,
            NormalizeOptional(request.Comment) ?? "Инцидент создан вручную.",
            request.Actor.Trim(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ManualIncidentCreationResult(
            ManualIncidentCreationOutcome.Created,
            incidentId);
    }

    public async Task<IncidentStatusChangeOutcome> ChangeStatusAsync(
        Guid incidentId,
        IncidentStatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
            UPDATE incidents
            SET status = $2,
                sent_to_provider_at_utc = CASE
                    WHEN $2 = 'SentToProvider' THEN coalesce(sent_to_provider_at_utc, $3)
                    ELSE sent_to_provider_at_utc
                END,
                recovered_at_utc = CASE
                    WHEN $2 = 'Resolved' THEN coalesce(recovered_at_utc, $3)
                    WHEN $2 = 'InProgress' AND status = 'Resolved' THEN NULL
                    ELSE recovered_at_utc
                END,
                closed_at_utc = CASE
                    WHEN $2 = 'Closed' THEN $3
                    WHEN $2 = 'InProgress' AND status = 'Resolved' THEN NULL
                    ELSE closed_at_utc
                END,
                updated_at_utc = $3
            WHERE id = $1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetIncidentStateForUpdateAsync(
            connection,
            transaction,
            incidentId,
            cancellationToken);
        if (state is null)
        {
            return IncidentStatusChangeOutcome.NotFound;
        }

        if (!IncidentStatusTransitionPolicy.CanTransition(state.Status, request.Status))
        {
            return IncidentStatusChangeOutcome.InvalidTransition;
        }

        if (state.Status == request.Status)
        {
            await transaction.CommitAsync(cancellationToken);
            return IncidentStatusChangeOutcome.Updated;
        }

        var occurredAtUtc = timeProvider.GetUtcNow();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = updateSql;
            command.Parameters.AddWithValue(incidentId);
            command.Parameters.AddWithValue(request.Status.ToString());
            command.Parameters.AddWithValue(occurredAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return IncidentStatusChangeOutcome.InvalidTransition;
        }

        await AddHistoryAsync(
            connection,
            transaction,
            incidentId,
            occurredAtUtc,
            "StatusChanged",
            state.Status,
            request.Status,
            NormalizeOptional(request.Comment),
            request.Actor.Trim(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IncidentStatusChangeOutcome.Updated;
    }

    public async Task<bool> AssignAsync(
        Guid incidentId,
        IncidentAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        const string updateSql = """
            UPDATE incidents
            SET assigned_to = $2, updated_at_utc = $3
            WHERE id = $1;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await GetIncidentStateForUpdateAsync(
            connection,
            transaction,
            incidentId,
            cancellationToken);
        if (state is null)
        {
            return false;
        }

        var assignedTo = NormalizeOptional(request.AssignedTo);
        var occurredAtUtc = timeProvider.GetUtcNow();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = updateSql;
            command.Parameters.AddWithValue(incidentId);
            AddNullableParameter(command, NpgsqlDbType.Text, assignedTo);
            command.Parameters.AddWithValue(occurredAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var assignmentDescription = $"Ответственный: {FormatAssignment(state.AssignedTo)} → {FormatAssignment(assignedTo)}.";
        var comment = NormalizeOptional(request.Comment);
        await AddHistoryAsync(
            connection,
            transaction,
            incidentId,
            occurredAtUtc,
            "AssignmentChanged",
            previousStatus: null,
            newStatus: null,
            comment is null ? assignmentDescription : $"{assignmentDescription} {comment}",
            request.Actor.Trim(),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddCommentAsync(
        Guid incidentId,
        IncidentCommentCreateRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO incident_history (
                incident_id, occurred_at_utc, action, comment, actor)
            SELECT $1, $2, 'CommentAdded', $3, $4
            WHERE EXISTS (SELECT 1 FROM incidents WHERE id = $1);
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        command.Parameters.AddWithValue(request.Comment.Trim());
        command.Parameters.AddWithValue(request.Actor.Trim());
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task AcquireLineLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended($1, 0));";
        command.Parameters.AddWithValue(lineId.ToString("D"));
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<List<IncidentSignalData>> GetSignalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT event_id, school_id, line_id, measured_at_utc, connection_status,
                   download_mbps, upload_mbps, ping_milliseconds, jitter_milliseconds,
                   packet_loss_percent, threshold_download_mbps, threshold_upload_mbps,
                   threshold_ping_milliseconds, threshold_jitter_milliseconds,
                   threshold_packet_loss_percent
            FROM measurements
            WHERE line_id = $1
            ORDER BY measured_at_utc DESC, received_at_utc DESC
            LIMIT $2;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(lineId);
        command.Parameters.AddWithValue(options.Incidents.MaximumSignalsToEvaluate);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var signals = new List<IncidentSignalData>();
        while (await reader.ReadAsync(cancellationToken))
        {
            signals.Add(new IncidentSignalData(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                new DateTimeOffset(reader.GetDateTime(3)),
                Enum.Parse<ConnectionStatus>(reader.GetString(4)),
                GetNullableDouble(reader, 5),
                GetNullableDouble(reader, 6),
                GetNullableDouble(reader, 7),
                GetNullableDouble(reader, 8),
                GetNullableDouble(reader, 9),
                Convert.ToDouble(reader.GetDecimal(10)),
                Convert.ToDouble(reader.GetDecimal(11)),
                Convert.ToDouble(reader.GetDecimal(12)),
                Convert.ToDouble(reader.GetDecimal(13)),
                Convert.ToDouble(reader.GetDecimal(14))));
        }

        return signals;
    }

    private static async Task<OpenIncident?> GetOpenIncidentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid lineId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, status
            FROM incidents
            WHERE line_id = $1 AND status NOT IN ('Resolved', 'Closed')
            ORDER BY started_at_utc DESC
            LIMIT 1
            FOR UPDATE;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(lineId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new OpenIncident(reader.GetGuid(0), Enum.Parse<IncidentStatus>(reader.GetString(1)))
            : null;
    }

    private static async Task<IncidentState?> GetIncidentStateForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT status, assigned_to
            FROM incidents
            WHERE id = $1
            FOR UPDATE;
            """;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(incidentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new IncidentState(
                Enum.Parse<IncidentStatus>(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1))
            : null;
    }

    private async Task<IncidentNotificationEvent> CreateIncidentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<IncidentSignalData> signals,
        CancellationToken cancellationToken)
    {
        const string incidentSql = """
            INSERT INTO incidents (
                id, school_id, line_id, source, problem_type, status, title,
                description, started_at_utc, detected_at_utc, latest_measurement_event_id)
            VALUES ($1, $2, $3, 'Automatic', $4, 'New', $5, $6, $7, $8, $9);
            """;

        var problemSignals = signals.TakeWhile(signal => signal.Signal.IsProblem).ToArray();
        var latest = problemSignals[0];
        var (problemType, title, description) = DescribeProblem(latest);
        var incidentId = Guid.NewGuid();
        var occurredAtUtc = timeProvider.GetUtcNow();
        var startedAtUtc = problemSignals[^1].Signal.MeasuredAtUtc > occurredAtUtc
            ? occurredAtUtc
            : problemSignals[^1].Signal.MeasuredAtUtc;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = incidentSql;
            command.Parameters.AddWithValue(incidentId);
            command.Parameters.AddWithValue(latest.SchoolId);
            command.Parameters.AddWithValue(latest.LineId);
            command.Parameters.AddWithValue(problemType);
            command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue(description);
            command.Parameters.AddWithValue(startedAtUtc);
            command.Parameters.AddWithValue(occurredAtUtc);
            command.Parameters.AddWithValue(latest.Signal.EventId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await AddHistoryAsync(
            connection,
            transaction,
            incidentId,
            occurredAtUtc,
            "CreatedAutomatically",
            previousStatus: null,
            IncidentStatus.New,
            $"Инцидент создан после {problemSignals.Length} последовательных проблемных измерений.",
            "system",
            cancellationToken);
        return new IncidentNotificationEvent(incidentId, IncidentNotificationKind.Opened);
    }

    private async Task<IncidentNotificationEvent> ResolveIncidentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OpenIncident incident,
        IncidentSignalData latest,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE incidents
            SET status = 'Resolved',
                recovered_at_utc = $2,
                latest_measurement_event_id = $3,
                updated_at_utc = $4
            WHERE id = $1;
            """;
        var occurredAtUtc = timeProvider.GetUtcNow();
        var recoveredAtUtc = latest.Signal.MeasuredAtUtc > occurredAtUtc
            ? occurredAtUtc
            : latest.Signal.MeasuredAtUtc;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue(incident.IncidentId);
            command.Parameters.AddWithValue(recoveredAtUtc);
            command.Parameters.AddWithValue(latest.Signal.EventId);
            command.Parameters.AddWithValue(occurredAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await AddHistoryAsync(
            connection,
            transaction,
            incident.IncidentId,
            occurredAtUtc,
            "RecoveredAutomatically",
            incident.Status,
            IncidentStatus.Resolved,
            $"Нормативные показатели восстановлены после {options.Incidents.ConsecutiveRecoveryMeasurements} последовательных успешных измерений.",
            "system",
            cancellationToken);
        return new IncidentNotificationEvent(incident.IncidentId, IncidentNotificationKind.Recovered);
    }

    private async Task UpdateLatestEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid incidentId,
        IncidentSignalData latest,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE incidents
            SET latest_measurement_event_id = $2,
                updated_at_utc = $3
            WHERE id = $1;
            """;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(latest.Signal.EventId);
        command.Parameters.AddWithValue(timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AddHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid incidentId,
        DateTimeOffset occurredAtUtc,
        string action,
        IncidentStatus? previousStatus,
        IncidentStatus? newStatus,
        string? comment,
        string? actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO incident_history (
                incident_id, occurred_at_utc, action, previous_status,
                new_status, comment, actor)
            VALUES ($1, $2, $3, $4, $5, $6, $7);
            """;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(incidentId);
        command.Parameters.AddWithValue(occurredAtUtc);
        command.Parameters.AddWithValue(action);
        AddNullableParameter(command, NpgsqlDbType.Text, previousStatus?.ToString());
        AddNullableParameter(command, NpgsqlDbType.Text, newStatus?.ToString());
        AddNullableParameter(command, NpgsqlDbType.Text, comment);
        AddNullableParameter(command, NpgsqlDbType.Text, actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static (string ProblemType, string Title, string Description) DescribeProblem(
        IncidentSignalData signal)
    {
        var problems = new List<(string Type, string Description)>();
        if (signal.ConnectionStatus == ConnectionStatus.Offline)
        {
            problems.Add(("NoConnection", "отсутствует подключение к Интернету"));
        }
        else if (signal.ConnectionStatus == ConnectionStatus.Degraded)
        {
            problems.Add(("DegradedConnection", "агент сообщил о неполном измерении"));
        }

        AddLowerProblem(problems, "DownloadBelowThreshold", "Download", signal.DownloadMbps, signal.MinimumDownloadMbps);
        AddLowerProblem(problems, "UploadBelowThreshold", "Upload", signal.UploadMbps, signal.MinimumUploadMbps);
        AddUpperProblem(problems, "HighPing", "Ping", signal.PingMilliseconds, signal.MaximumPingMilliseconds);
        AddUpperProblem(problems, "HighJitter", "Jitter", signal.JitterMilliseconds, signal.MaximumJitterMilliseconds);
        AddUpperProblem(problems, "PacketLoss", "Packet Loss", signal.PacketLossPercent, signal.MaximumPacketLossPercent);

        var problemType = problems.Count switch
        {
            0 => "MissingMetrics",
            1 => problems[0].Type,
            _ => "MultipleMetrics"
        };
        var description = problems.Count == 0
            ? "Обнаружено устойчивое отклонение качества соединения."
            : string.Join("; ", problems.Select(problem => problem.Description)) + ".";
        return (problemType, "Устойчивое ухудшение качества Интернет-соединения", description);
    }

    private static void AddLowerProblem(
        ICollection<(string Type, string Description)> problems,
        string type,
        string name,
        double? value,
        double threshold)
    {
        if (value is not null && value.Value < threshold)
        {
            problems.Add((type, $"{name} {Format(value.Value)} ниже порога {Format(threshold)}"));
        }
    }

    private static void AddUpperProblem(
        ICollection<(string Type, string Description)> problems,
        string type,
        string name,
        double? value,
        double threshold)
    {
        if (value is not null && value.Value > threshold)
        {
            problems.Add((type, $"{name} {Format(value.Value)} выше порога {Format(threshold)}"));
        }
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static IncidentOverview ReadIncident(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetGuid(2),
        reader.GetString(3),
        reader.GetGuid(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        Enum.Parse<IncidentSource>(reader.GetString(7)),
        reader.GetString(8),
        Enum.Parse<IncidentStatus>(reader.GetString(9)),
        reader.GetString(10),
        reader.GetString(11),
        new DateTimeOffset(reader.GetDateTime(12)),
        new DateTimeOffset(reader.GetDateTime(13)),
        reader.IsDBNull(14) ? null : new DateTimeOffset(reader.GetDateTime(14)),
        reader.IsDBNull(15) ? null : new DateTimeOffset(reader.GetDateTime(15)),
        reader.IsDBNull(16) ? null : new DateTimeOffset(reader.GetDateTime(16)),
        reader.GetInt64(17),
        reader.IsDBNull(18) ? null : reader.GetGuid(18),
        reader.IsDBNull(19) ? null : reader.GetString(19));

    private static void AddNullableParameter(
        NpgsqlCommand command,
        NpgsqlDbType type,
        object? value)
    {
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = type,
            Value = value ?? DBNull.Value
        });
    }

    private static double? GetNullableDouble(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetDecimal(ordinal));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatAssignment(string? value) =>
        value is null ? "не назначен" : $"«{value}»";

    private sealed record OpenIncident(Guid IncidentId, IncidentStatus Status);

    private sealed record IncidentState(IncidentStatus Status, string? AssignedTo);

    private sealed record IncidentSignalData(
        Guid EventId,
        Guid SchoolId,
        Guid LineId,
        DateTimeOffset MeasuredAtUtc,
        ConnectionStatus ConnectionStatus,
        double? DownloadMbps,
        double? UploadMbps,
        double? PingMilliseconds,
        double? JitterMilliseconds,
        double? PacketLossPercent,
        double MinimumDownloadMbps,
        double MinimumUploadMbps,
        double MaximumPingMilliseconds,
        double MaximumJitterMilliseconds,
        double MaximumPacketLossPercent)
    {
        public IncidentSignal Signal => new(EventId, MeasuredAtUtc, IsProblem);

        private bool IsProblem =>
            ConnectionStatus != VkoMonitoring.Agent.Core.Domain.ConnectionStatus.Online ||
            DownloadMbps is null ||
            UploadMbps is null ||
            PingMilliseconds is null ||
            JitterMilliseconds is null ||
            PacketLossPercent is null ||
            DownloadMbps < MinimumDownloadMbps ||
            UploadMbps < MinimumUploadMbps ||
            PingMilliseconds > MaximumPingMilliseconds ||
            JitterMilliseconds > MaximumJitterMilliseconds ||
            PacketLossPercent > MaximumPacketLossPercent;
    }
}
