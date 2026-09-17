using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Health;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Security;
using VkoMonitoring.Api.Validation;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var options = builder.Configuration
    .GetRequiredSection(MonitoringApiOptions.SectionName)
    .Get<MonitoringApiOptions>()
    ?? throw new InvalidOperationException("Monitoring API configuration is missing.");

var postgresConnectionString = builder.Configuration.GetConnectionString("MonitoringDatabase");
MonitoringApiOptionsValidator.Validate(options, postgresConnectionString);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TokenValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddPolicy("device-activation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
if (options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(postgresConnectionString!));
    builder.Services.AddSingleton<PostgresDatabaseInitializer>();
    builder.Services.AddSingleton<IMeasurementRepository, PostgresMeasurementRepository>();
    builder.Services.AddSingleton<IDevicePresenceRepository, PostgresDevicePresenceRepository>();
    builder.Services.AddSingleton<IMonitoringReadRepository, PostgresMonitoringReadRepository>();
    builder.Services.AddSingleton<IDeviceAuthenticator, PostgresDeviceAuthenticator>();
    builder.Services.AddSingleton<IDeviceRegistrationRepository, PostgresDeviceRegistrationRepository>();
    builder.Services.AddSingleton<IDeviceActivationRepository, PostgresDeviceActivationRepository>();
    builder.Services.AddSingleton<IIncidentRepository, PostgresIncidentRepository>();
    builder.Services.AddSingleton<IApiReadinessProbe, PostgresReadinessProbe>();
}
else
{
    builder.Services.AddSingleton<IMeasurementRepository, JsonFileMeasurementRepository>();
    builder.Services.AddSingleton<IDevicePresenceRepository, JsonFileDevicePresenceRepository>();
    builder.Services.AddSingleton<IMonitoringReadRepository, JsonFileMonitoringReadRepository>();
    builder.Services.AddSingleton<IDeviceAuthenticator, ConfigurationDeviceAuthenticator>();
    builder.Services.AddSingleton<IDeviceRegistrationRepository, UnsupportedDeviceRegistrationRepository>();
    builder.Services.AddSingleton<IDeviceActivationRepository, UnsupportedDeviceActivationRepository>();
    builder.Services.AddSingleton<IIncidentRepository, UnsupportedIncidentRepository>();
    builder.Services.AddSingleton<IApiReadinessProbe, LocalStorageReadinessProbe>();
}

var app = builder.Build();
app.UseRateLimiter();

if (options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    await app.Services
        .GetRequiredService<PostgresDatabaseInitializer>()
        .InitializeAsync(CancellationToken.None);
}

app.MapGet("/health/live", (TimeProvider timeProvider) => Results.Ok(new
{
    status = ApiHealthStatus.Healthy.ToString(),
    checkedAtUtc = timeProvider.GetUtcNow()
}));

app.MapGet("/health/ready", CheckReadinessAsync);
app.MapGet("/health", CheckReadinessAsync);

app.MapPost(
    "/api/devices/heartbeat",
    async (
        AgentHeartbeat heartbeat,
        HttpRequest request,
        IDeviceAuthenticator authenticator,
        IDevicePresenceRepository repository,
        CancellationToken cancellationToken) =>
    {
        var suppliedToken = request.Headers["X-Device-Token"].FirstOrDefault();
        if (!await authenticator.IsAuthorizedAsync(
                heartbeat.SchoolId,
                heartbeat.DeviceId,
                heartbeat.LineId,
                suppliedToken,
                cancellationToken))
        {
            return Results.Unauthorized();
        }

        if (heartbeat.SentAtUtc == default || string.IsNullOrWhiteSpace(heartbeat.AgentVersion))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(heartbeat.SentAtUtc)] = ["Heartbeat timestamp and agent version are required."]
            });
        }

        return await repository.RecordHeartbeatAsync(heartbeat, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    });

app.MapGet(
    "/api/devices/{deviceId:guid}/status",
    async (
        Guid deviceId,
        Guid schoolId,
        Guid lineId,
        HttpRequest request,
        IDeviceAuthenticator authenticator,
        IMonitoringReadRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var suppliedToken = request.Headers["X-Device-Token"].FirstOrDefault();
        if (!await authenticator.IsAuthorizedAsync(
                schoolId,
                deviceId,
                lineId,
                suppliedToken,
                cancellationToken))
        {
            return Results.Unauthorized();
        }

        var device = await repository.GetDeviceAsync(deviceId, cancellationToken);
        if (device is null)
        {
            return Results.NotFound();
        }

        var measurements = await repository.GetDeviceMeasurementsAsync(
            deviceId,
            timeProvider.GetUtcNow().AddDays(-7),
            timeProvider.GetUtcNow().AddMinutes(1),
            20,
            cancellationToken);
        return Results.Ok(new VkoMonitoring.Api.Models.LocalDeviceStatus(device, measurements));
    });

app.MapPost(
    "/api/devices/register",
    async (
        VkoMonitoring.Api.Models.DeviceRegistrationRequest registration,
        HttpRequest request,
        TokenValidator tokenValidator,
        IDeviceRegistrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Device registration requires PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(registration.SchoolName))
        {
            errors[nameof(registration.SchoolName)] = ["School name is required."];
        }

        if (string.IsNullOrWhiteSpace(registration.LineName))
        {
            errors[nameof(registration.LineName)] = ["Line name is required."];
        }

        if (string.IsNullOrWhiteSpace(registration.DeviceName))
        {
            errors[nameof(registration.DeviceName)] = ["Device name is required."];
        }

        if (registration.LineStatus is not ("Primary" or "Backup" or "Disabled"))
        {
            errors[nameof(registration.LineStatus)] = ["Line status must be Primary, Backup, or Disabled."];
        }

        if (registration.ContractedDownloadMbps is <= 0 || registration.ContractedUploadMbps is <= 0)
        {
            errors["contractedSpeed"] = ["Contracted speeds must be greater than zero when specified."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var schoolId = registration.SchoolId.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : registration.SchoolId.GetValueOrDefault();
        var lineId = registration.LineId.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : registration.LineId.GetValueOrDefault();
        var deviceId = registration.DeviceId.GetValueOrDefault() == Guid.Empty
            ? Guid.NewGuid()
            : registration.DeviceId.GetValueOrDefault();
        var deviceIdentifier = string.IsNullOrWhiteSpace(registration.DeviceIdentifier)
            ? deviceId.ToString("D")
            : registration.DeviceIdentifier.Trim();
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            await repository.RegisterAsync(
                registration,
                schoolId,
                lineId,
                deviceId,
                deviceIdentifier,
                DeviceTokenHasher.Hash(deviceToken),
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { error = "Device identifier is already registered." });
        }

        return Results.Created(
            $"/api/schools/{schoolId}/devices",
            new VkoMonitoring.Api.Models.DeviceRegistrationResult(
                schoolId,
                lineId,
                deviceId,
                deviceIdentifier,
                deviceToken));
    });

app.MapPost(
    "/api/activation-codes",
    async (
        VkoMonitoring.Api.Models.ActivationCodeCreateRequest request,
        HttpRequest httpRequest,
        TokenValidator tokenValidator,
        IDeviceActivationRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(httpRequest.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Device activation requires PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var lifetimeMinutes = request.LifetimeMinutes ?? 30;
        if (request.SchoolId == Guid.Empty || request.LineId == Guid.Empty || lifetimeMinutes is < 5 or > 1_440)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["activationCode"] = ["School, line and a lifetime between 5 and 1440 minutes are required."]
            });
        }

        var activationCode = ActivationCodeProtector.Generate();
        var expiresAtUtc = timeProvider.GetUtcNow().AddMinutes(lifetimeMinutes);
        var created = await repository.CreateCodeAsync(
            Guid.NewGuid(),
            request.SchoolId,
            request.LineId,
            ActivationCodeProtector.Hash(activationCode),
            expiresAtUtc,
            cancellationToken);
        if (!created)
        {
            return Results.NotFound(new { error = "School and line binding was not found." });
        }

        return Results.Created(
            "/api/activation-codes",
            new VkoMonitoring.Api.Models.ActivationCodeCreateResult(activationCode, expiresAtUtc));
    });

app.MapPost(
    "/api/devices/activate",
    async (
        VkoMonitoring.Api.Models.DeviceActivationRequest request,
        IDeviceActivationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Device activation requires PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (!ActivationCodeProtector.IsValidFormat(request.ActivationCode) ||
            string.IsNullOrWhiteSpace(request.DeviceName) ||
            request.DeviceName.Length > 200 ||
            request.DeviceIdentifier?.Length > 200 ||
            request.Room?.Length > 200 ||
            request.ConnectionType?.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["activation"] = ["Activation code and valid device information are required."]
            });
        }

        var deviceId = Guid.NewGuid();
        var deviceIdentifier = string.IsNullOrWhiteSpace(request.DeviceIdentifier)
            ? deviceId.ToString("D")
            : request.DeviceIdentifier.Trim();
        var deviceToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            var binding = await repository.ActivateAsync(
                request,
                deviceId,
                deviceIdentifier,
                ActivationCodeProtector.Hash(request.ActivationCode),
                DeviceTokenHasher.Hash(deviceToken),
                cancellationToken);
            if (binding is null)
            {
                return Results.Json(
                    new { error = "Activation code is invalid, expired, or already used." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Created(
                $"/api/schools/{binding.SchoolId}/devices",
                new VkoMonitoring.Api.Models.DeviceActivationResult(
                    binding.SchoolId,
                    binding.LineId,
                    binding.DeviceId,
                    binding.DeviceIdentifier,
                    deviceToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { error = "Device identifier is already registered." });
        }
    })
    .RequireRateLimiting("device-activation");

app.MapPost(
    "/api/measurements",
    async (
        InternetMeasurement measurement,
        HttpRequest request,
        IDeviceAuthenticator authenticator,
        IMeasurementRepository repository,
        IIncidentRepository incidentRepository,
        IDevicePresenceRepository presenceRepository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var suppliedToken = request.Headers["X-Device-Token"].FirstOrDefault();
        if (!await authenticator.IsAuthorizedAsync(
                measurement.SchoolId,
                measurement.DeviceId,
                measurement.LineId,
                suppliedToken,
                cancellationToken))
        {
            return Results.Unauthorized();
        }

        var errors = MeasurementValidator.Validate(measurement);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var serverObservedMeasurement = measurement with
        {
            ExternalIpAddress = request.HttpContext.Connection.RemoteIpAddress?
                .MapToIPv4()
                .ToString()
        };
        var wasCreated = await repository.AddIfNotExistsAsync(serverObservedMeasurement, cancellationToken);
        await incidentRepository.ProcessLatestMeasurementAsync(
            serverObservedMeasurement.LineId,
            cancellationToken);
        await presenceRepository.RecordHeartbeatAsync(
            new AgentHeartbeat(
                measurement.SchoolId,
                measurement.DeviceId,
                measurement.LineId,
                timeProvider.GetUtcNow(),
                measurement.AgentVersion),
            cancellationToken);
        return wasCreated
            ? Results.Created($"/api/measurements/{measurement.EventId}", new { measurement.EventId })
            : Results.Ok(new { measurement.EventId, duplicate = true });
    });

app.MapGet(
    "/api/incidents",
    async (
        Guid? schoolId,
        IncidentStatus? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        HttpRequest request,
        TokenValidator tokenValidator,
        IIncidentRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Incidents require PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var toUtc = to ?? timeProvider.GetUtcNow().AddMinutes(1);
        var fromUtc = from ?? toUtc.AddDays(-30);
        if (fromUtc >= toUtc)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["The start of the period must be earlier than the end."]
            });
        }

        return Results.Ok(await repository.GetIncidentsAsync(
            schoolId,
            status,
            fromUtc,
            toUtc,
            Math.Clamp(limit ?? 200, 1, 1_000),
            cancellationToken));
    });

app.MapGet(
    "/api/incidents/{incidentId:guid}",
    async (
        Guid incidentId,
        HttpRequest request,
        TokenValidator tokenValidator,
        IIncidentRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Incidents require PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var incident = await repository.GetIncidentAsync(incidentId, cancellationToken);
        return incident is null ? Results.NotFound() : Results.Ok(incident);
    });

app.MapGet(
    "/api/measurements/recent",
    async (
        int? limit,
        HttpRequest request,
        TokenValidator tokenValidator,
        IMeasurementRepository repository,
        CancellationToken cancellationToken) =>
    {
        var suppliedToken = request.Headers["X-Admin-Token"].FirstOrDefault();
        if (!tokenValidator.IsAdminAuthorized(suppliedToken))
        {
            return Results.Unauthorized();
        }

        var safeLimit = Math.Clamp(limit ?? 100, 1, 1_000);
        return Results.Ok(await repository.GetRecentAsync(safeLimit, cancellationToken));
    });

app.MapGet(
    "/api/schools",
    async (
        HttpRequest request,
        TokenValidator tokenValidator,
        IMonitoringReadRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await repository.GetSchoolsAsync(cancellationToken));
    });

app.MapGet(
    "/api/schools/{schoolId:guid}",
    async (
        Guid schoolId,
        HttpRequest request,
        TokenValidator tokenValidator,
        IMonitoringReadRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        var school = await repository.GetSchoolAsync(schoolId, cancellationToken);
        return school is null ? Results.NotFound() : Results.Ok(school);
    });

app.MapGet(
    "/api/schools/{schoolId:guid}/devices",
    async (
        Guid schoolId,
        HttpRequest request,
        TokenValidator tokenValidator,
        IMonitoringReadRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (await repository.GetSchoolAsync(schoolId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await repository.GetSchoolDevicesAsync(schoolId, cancellationToken));
    });

app.MapGet(
    "/api/devices/{deviceId:guid}/measurements",
    async (
        Guid deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        HttpRequest request,
        TokenValidator tokenValidator,
        IMonitoringReadRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        var toUtc = to ?? timeProvider.GetUtcNow();
        var fromUtc = from ?? toUtc.AddDays(-30);
        if (fromUtc >= toUtc)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["The start of the period must be earlier than the end."]
            });
        }

        return Results.Ok(await repository.GetDeviceMeasurementsAsync(
            deviceId,
            fromUtc,
            toUtc,
            Math.Clamp(limit ?? 500, 1, 5_000),
            cancellationToken));
    });

app.MapGet(
    "/api/analytics",
    async (
        Guid? schoolId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        HttpRequest request,
        TokenValidator tokenValidator,
        IMonitoringReadRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        var toUtc = to ?? timeProvider.GetUtcNow();
        var fromUtc = from ?? toUtc.AddDays(-1);
        if (fromUtc >= toUtc)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["The start of the period must be earlier than the end."]
            });
        }

        return Results.Ok(await repository.GetAnalyticsAsync(
            schoolId,
            fromUtc,
            toUtc,
            cancellationToken));
    });

app.MapGet(
    "/speed/download",
    async (int? bytes, HttpResponse response, CancellationToken cancellationToken) =>
    {
        var responseBytes = Math.Clamp(bytes ?? 5_000_000, 1_024, options.MaximumSpeedTestBytes);
        var buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
        RandomNumberGenerator.Fill(buffer);
        response.ContentType = "application/octet-stream";
        response.ContentLength = responseBytes;

        var remaining = responseBytes;
        while (remaining > 0)
        {
            var count = Math.Min(buffer.Length, remaining);
            await response.Body.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            remaining -= count;
        }
    });

app.MapPost(
    "/speed/upload",
    async (HttpRequest request, CancellationToken cancellationToken) =>
    {
        if (request.ContentLength > options.MaximumSpeedTestBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
        var totalBytes = 0L;
        int bytesRead;
        while ((bytesRead = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > options.MaximumSpeedTestBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
        }

        return Results.NoContent();
    });

app.Run();

static async Task<IResult> CheckReadinessAsync(
    IApiReadinessProbe readinessProbe,
    CancellationToken cancellationToken)
{
    var report = await readinessProbe.CheckAsync(cancellationToken);
    var statusCode = report.Status == ApiHealthStatus.Unavailable.ToString()
        ? StatusCodes.Status503ServiceUnavailable
        : StatusCodes.Status200OK;
    return Results.Json(report, statusCode: statusCode);
}
