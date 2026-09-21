using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Health;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Security;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Api.Validation;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

var mapConfiguration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "wwwroot", "map-config.json"))
    .Build();
var contentSecurityPolicy = MapContentSecurityPolicy.Create(mapConfiguration["tileUrl"]
    ?? throw new InvalidOperationException("Map tile URL is missing."));

var options = builder.Configuration
    .GetRequiredSection(MonitoringApiOptions.SectionName)
    .Get<MonitoringApiOptions>()
    ?? throw new InvalidOperationException("Monitoring API configuration is missing.");
var appealDraftOptions = builder.Configuration
    .GetSection(OpenAiAppealDraftOptions.SectionName)
    .Get<OpenAiAppealDraftOptions>() ?? new OpenAiAppealDraftOptions();

var postgresConnectionString = PostgresConnectionStringResolver.Resolve(
    builder.Configuration.GetConnectionString("MonitoringDatabase"));
MonitoringApiOptionsValidator.Validate(options, postgresConnectionString);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(appealDraftOptions);
builder.Services.AddHttpClient<IAppealDraftGenerator, OpenAiAppealDraftGenerator>(client =>
{
    client.BaseAddress = new Uri(appealDraftOptions.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(appealDraftOptions.TimeoutSeconds, 5, 60));
});
builder.Services.AddScoped<RatingService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<MonitoringAccessContext>();
builder.Services.AddSingleton<TokenValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersOptions =>
{
    forwardedHeadersOptions.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwardedHeadersOptions.ForwardedForHeaderName = "CF-Connecting-IP";
    forwardedHeadersOptions.ForwardLimit = 1;
    forwardedHeadersOptions.KnownProxies.Clear();
    forwardedHeadersOptions.KnownProxies.Add(IPAddress.Loopback);
    forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddPolicy("user-login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    rateLimiterOptions.AddPolicy("device-activation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("speed-test", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("agent-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("agent-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("admin-read", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.AddPolicy("admin-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            ClientRateLimitPartition.GetKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.OnRejected = async (rejected, ct) =>
    {
        var context = rejected.HttpContext;
        context.Response.StatusCode = 429;
        var repository = context.RequestServices.GetService<PostgresUserRepository>();
        if (repository is not null)
        {
            var id = await repository.BeginAuditAsync(null, "anonymous", "RateLimit",
                context.Request.Path.Value ?? "/", context.Connection.RemoteIpAddress?.ToString(), ct);
            await repository.CompleteAuditAsync(id, 429, CancellationToken.None);
        }
    };
});
if (options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(postgresConnectionString!));
    builder.Services.AddSingleton<PostgresSchoolLocationRepository>();
    builder.Services.AddSingleton<PostgresDirectoryRepository>();
    builder.Services.AddSingleton<PostgresDatabaseInitializer>();
    builder.Services.AddSingleton<PostgresUserRepository>();
    builder.Services.AddSingleton<IMeasurementRepository, PostgresMeasurementRepository>();
    builder.Services.AddSingleton<IDevicePresenceRepository, PostgresDevicePresenceRepository>();
    builder.Services.AddSingleton<IMonitoringReadRepository, PostgresMonitoringReadRepository>();
    builder.Services.AddSingleton<IDeviceAuthenticator, PostgresDeviceAuthenticator>();
    builder.Services.AddSingleton<IDeviceRegistrationRepository, PostgresDeviceRegistrationRepository>();
    builder.Services.AddSingleton<IDeviceAdministrationRepository, PostgresDeviceAdministrationRepository>();
    builder.Services.AddSingleton<IDeviceActivationRepository, PostgresDeviceActivationRepository>();
    builder.Services.AddSingleton<IOperationalSettingsRepository, PostgresOperationalSettingsRepository>();
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
    builder.Services.AddSingleton<IDeviceAdministrationRepository, UnsupportedDeviceAdministrationRepository>();
    builder.Services.AddSingleton<IDeviceActivationRepository, UnsupportedDeviceActivationRepository>();
    builder.Services.AddSingleton<IOperationalSettingsRepository, ConfiguredOperationalSettingsRepository>();
    builder.Services.AddSingleton<IIncidentRepository, UnsupportedIncidentRepository>();
    builder.Services.AddSingleton<IApiReadinessProbe, LocalStorageReadinessProbe>();
}

var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] =
        contentSecurityPolicy;
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});
app.Use(async (httpContext, next) =>
{
    var maximumRequestBytes = RequestBodySizePolicy.GetMaximumBytes(
        httpContext.Request.Path,
        options.MaximumSpeedTestBytes);
    if (httpContext.Request.ContentLength > maximumRequestBytes)
    {
        httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    var requestBodySizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (requestBodySizeFeature is { IsReadOnly: false })
    {
        requestBodySizeFeature.MaxRequestBodySize = maximumRequestBytes;
    }

    await next(httpContext);
});
app.UseRouting();
app.UseRateLimiter();
app.UseMiddleware<UserAccessMiddleware>();
app.MapUserEndpoints();
app.MapSchoolLocationEndpoints();
app.MapDirectoryEndpoints();
app.MapReportEndpoints();

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
    })
    .RequireRateLimiting("agent-write");

app.MapGet(
    "/api/devices/configuration",
    async (Guid schoolId, Guid deviceId, Guid lineId, HttpRequest request, IDeviceAuthenticator authenticator,
        IOperationalSettingsRepository settingsRepository, CancellationToken cancellationToken) =>
    {
        if (!await authenticator.IsAuthorizedAsync(schoolId, deviceId, lineId,
            request.Headers["X-Device-Token"].FirstOrDefault(), cancellationToken)) return Results.Unauthorized();
        var settings = await settingsRepository.GetAsync(cancellationToken);
        return Results.Ok(new VkoMonitoring.Agent.Core.Domain.AgentRuntimeConfiguration(settings.MeasurementWindows));
    })
    .RequireRateLimiting("agent-write");

app.MapGet("/api/settings/operations", async (HttpRequest request, IOperationalSettingsRepository repository, CancellationToken cancellationToken) =>
    MonitoringRequestAccess.From(request.HttpContext)?.User is { Role: not UserRole.Administrator }
        ? Results.StatusCode(StatusCodes.Status403Forbidden)
        : Results.Ok(await repository.GetAsync(cancellationToken)))
    .RequireRateLimiting("admin-read");

app.MapPut("/api/settings/operations", async (
    VkoMonitoring.Api.Models.OperationalSettings settings,
    HttpRequest request,
    IOperationalSettingsRepository repository,
    CancellationToken cancellationToken) =>
{
    if (MonitoringRequestAccess.From(request.HttpContext)?.User is { Role: not UserRole.Administrator }) return Results.StatusCode(StatusCodes.Status403Forbidden);
    try
    {
        if (settings.MeasurementWindows is null || settings.MeasurementWindows.Length is < 1 or > 12 ||
            settings.MeasurementWindows.Any(window => string.IsNullOrWhiteSpace(window)) ||
            settings.MeasurementWindows.Select(VkoMonitoring.Agent.Core.Configuration.DailyWindow.Parse).Count() == 0 ||
            settings.MinimumDownloadMbps <= 0 || settings.MinimumUploadMbps <= 0 ||
            settings.MaximumPingMilliseconds <= 0 || settings.MaximumJitterMilliseconds <= 0 ||
            settings.MaximumPacketLossPercent is <= 0 or > 100 || settings.MinimumAvailabilityPercent is <= 0 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Specify valid measurement windows and positive quality thresholds."] });
        return Results.Ok(await repository.UpdateAsync(settings, cancellationToken));
    }
    catch (FormatException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["measurementWindows"] = ["Use HH:mm-HH:mm and a non-empty interval."] });
    }
})
    .RequireRateLimiting("admin-write");

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
    })
    .RequireRateLimiting("agent-read");

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
        var deviceToken = DeviceTokenIssuer.Generate();

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
    })
    .RequireRateLimiting("admin-write");

app.MapPut(
    "/api/devices/{deviceId:guid}/block-state",
    async (
        Guid deviceId,
        VkoMonitoring.Api.Models.DeviceBlockStateRequest blockState,
        HttpRequest request,
        TokenValidator tokenValidator,
        IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Device administration requires PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var updated = await repository.SetBlockedAsync(
            deviceId,
            blockState.IsBlocked,
            cancellationToken);
        return updated
            ? Results.Ok(new VkoMonitoring.Api.Models.DeviceBlockStateResult(
                deviceId,
                blockState.IsBlocked))
            : Results.NotFound();
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/devices/{deviceId:guid}/token/rotate",
    async (
        Guid deviceId,
        HttpRequest request,
        TokenValidator tokenValidator,
        IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault()))
        {
            return Results.Unauthorized();
        }

        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                "Device administration requires PostgreSQL storage.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var deviceToken = DeviceTokenIssuer.Generate();
        var updated = await repository.ReplaceTokenHashAsync(
            deviceId,
            DeviceTokenHasher.Hash(deviceToken),
            cancellationToken);
        return updated
            ? Results.Ok(new VkoMonitoring.Api.Models.DeviceTokenRotationResult(deviceId, deviceToken))
            : Results.NotFound();
    })
    .RequireRateLimiting("admin-write");

app.MapPut(
    "/api/devices/{deviceId:guid}/binding",
    async (Guid deviceId, VkoMonitoring.Api.Models.DeviceRebindRequest binding,
        HttpRequest request, IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (MonitoringRequestAccess.From(request.HttpContext)?.User is { Role: not UserRole.Administrator })
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsValidLifecycleReason(binding.Reason) || binding.SchoolId == Guid.Empty || binding.LineId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["binding"] = ["School, line and a 5-500 character reason are required."] });
        var access = MonitoringRequestAccess.From(request.HttpContext);
        var result = await repository.RebindAsync(deviceId, binding.SchoolId, binding.LineId,
            binding.Reason, access?.Actor ?? "bootstrap-admin", cancellationToken);
        return result is null
            ? Results.Conflict(new { error = "Device is not active or the target school/line binding is invalid." })
            : Results.Ok(result);
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/devices/{deviceId:guid}/replace",
    async (Guid deviceId, VkoMonitoring.Api.Models.DeviceReplacementRequest replacement,
        HttpRequest request, IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (MonitoringRequestAccess.From(request.HttpContext)?.User is { Role: not UserRole.Administrator })
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsValidLifecycleReason(replacement.Reason) || replacement.ReplacementDeviceId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["replacement"] = ["Replacement device and a 5-500 character reason are required."] });
        var access = MonitoringRequestAccess.From(request.HttpContext);
        var result = await repository.ReplaceAsync(deviceId, replacement.ReplacementDeviceId,
            replacement.Reason, access?.Actor ?? "bootstrap-admin", cancellationToken);
        return result is null
            ? Results.Conflict(new { error = "Both devices must be active, different, and belong to the same school." })
            : Results.Ok(result);
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/devices/{deviceId:guid}/decommission",
    async (Guid deviceId, VkoMonitoring.Api.Models.DeviceDecommissionRequest retirement,
        HttpRequest request, IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
    {
        if (MonitoringRequestAccess.From(request.HttpContext)?.User is { Role: not UserRole.Administrator })
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsValidLifecycleReason(retirement.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A 5-500 character reason is required."] });
        var access = MonitoringRequestAccess.From(request.HttpContext);
        var result = await repository.DecommissionAsync(deviceId, retirement.Reason,
            access?.Actor ?? "bootstrap-admin", cancellationToken);
        return result is null
            ? Results.Conflict(new { error = "Device is missing or no longer active." })
            : Results.Ok(result);
    })
    .RequireRateLimiting("admin-write");

app.MapGet(
    "/api/devices/{deviceId:guid}/lifecycle",
    async (Guid deviceId, IDeviceAdministrationRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetHistoryAsync(deviceId, cancellationToken)))
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-write");

app.MapGet(
    "/api/activation-codes",
    async (int? limit, HttpRequest request, IDeviceActivationRepository repository, CancellationToken cancellationToken) =>
    {
        var user = MonitoringRequestAccess.From(request.HttpContext)?.User;
        if (user is not null && user.Role is not (UserRole.Administrator or UserRole.Regional))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem("Device activation requires PostgreSQL storage.", statusCode: StatusCodes.Status501NotImplemented);
        }
        return Results.Ok(await repository.ListCodesAsync(Math.Clamp(limit ?? 100, 1, 500), cancellationToken));
    })
    .RequireRateLimiting("admin-read");

app.MapPost(
    "/api/incidents/{incidentId:guid}/appeal-draft",
    async (Guid incidentId, HttpRequest request, TokenValidator tokenValidator,
        IIncidentRepository incidentRepository, IMonitoringReadRepository monitoringRepository,
        IAppealDraftGenerator generator, TimeProvider timeProvider, CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault())) return Results.Unauthorized();
        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("AI drafts require PostgreSQL storage.", statusCode: StatusCodes.Status501NotImplemented);
        var details = await incidentRepository.GetIncidentAsync(incidentId, cancellationToken);
        if (details is null) return Results.NotFound();
        var toUtc = timeProvider.GetUtcNow().AddMinutes(1);
        var fromUtc = details.Incident.StartedAtUtc > toUtc.AddDays(-90) ? details.Incident.StartedAtUtc : toUtc.AddDays(-90);
        var rows = await monitoringRepository.GetReportRowsAsync(
            new ReportFilter(details.Incident.SchoolId, [], fromUtc, toUtc, null), 10_000, cancellationToken);
        try
        {
            var draft = await generator.GenerateAsync(AppealDraftFactsFactory.Create(details.Incident,
                rows.Where(row => row.LineId == details.Incident.LineId)), cancellationToken);
            return Results.Ok(new { draft.Text, draft.Model, factsPeriodFromUtc = fromUtc, factsPeriodToUtc = toUtc });
        }
        catch (AppealDraftUnavailableException)
        {
            return Results.Problem("AI draft is temporarily unavailable or not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/activation-codes/{activationCodeId:guid}/revoke",
    async (Guid activationCodeId, IDeviceActivationRepository repository, CancellationToken cancellationToken) =>
    {
        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem("Device activation requires PostgreSQL storage.", statusCode: StatusCodes.Status501NotImplemented);
        }
        return await repository.RevokeCodeAsync(activationCodeId, cancellationToken)
            ? Results.NoContent()
            : Results.Conflict(new { error = "Code is missing, expired, used, or already revoked." });
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/devices/activation-preview",
    async (
        VkoMonitoring.Api.Models.ActivationCodePreviewRequest request,
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
            request.DeviceIdentifier?.Length > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["activationCode"] = ["A valid activation code is required."]
            });
        }

        var preview = await repository.PreviewAsync(
            ActivationCodeProtector.Hash(request.ActivationCode),
            string.IsNullOrWhiteSpace(request.DeviceIdentifier) ? null : request.DeviceIdentifier.Trim(),
            cancellationToken);
        return preview is null
            ? Results.Json(
                new { error = "Activation code is invalid, expired, or already used." },
                statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(preview);
    })
    .RequireRateLimiting("device-activation");

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
        var deviceToken = DeviceTokenIssuer.Generate();

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
                    deviceToken,
                    binding.Recovered));
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

        var errors = MeasurementValidator.Validate(
            measurement,
            timeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(options.MaximumMeasurementClockSkewMinutes));
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
    })
    .RequireRateLimiting("agent-write");

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
    })
    .RequireRateLimiting("admin-read");

app.MapGet(
    "/api/ratings",
    async (DateTimeOffset? from, DateTimeOffset? to, HttpRequest request, TokenValidator tokenValidator,
        RatingService ratings, TimeProvider timeProvider, CancellationToken cancellationToken) =>
    {
        if (!tokenValidator.IsAdminAuthorized(request.Headers["X-Admin-Token"].FirstOrDefault())) return Results.Unauthorized();
        if (!options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Ratings require PostgreSQL storage.", statusCode: StatusCodes.Status501NotImplemented);
        var toUtc = to ?? timeProvider.GetUtcNow().AddMinutes(1);
        var fromUtc = from ?? toUtc.AddDays(-30);
        if (fromUtc >= toUtc) return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["The start of the period must be earlier than the end."] });
        try { return Results.Ok(await ratings.BuildAsync(fromUtc, toUtc, cancellationToken)); }
        catch (InvalidOperationException) { return Results.Problem("Too many measurements for the selected period. Choose a shorter period.", statusCode: StatusCodes.Status422UnprocessableEntity); }
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

app.MapPost(
    "/api/incidents",
    async (
        VkoMonitoring.Api.Models.ManualIncidentCreateRequest incident,
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

        var incidentAccess = MonitoringRequestAccess.From(request.HttpContext)!;
        if (!incidentAccess.CanAccessLine(incident.LineId)) return Results.StatusCode(403);
        incident = incident with { Actor = incidentAccess.Actor };
        var errors = IncidentAdministrationValidator.Validate(incident, timeProvider.GetUtcNow());
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await repository.CreateManualIncidentAsync(incident, cancellationToken);
        return result.Outcome switch
        {
            ManualIncidentCreationOutcome.Created => Results.Created(
                $"/api/incidents/{result.IncidentId}",
                new VkoMonitoring.Api.Models.ManualIncidentCreateResult(result.IncidentId!.Value)),
            ManualIncidentCreationOutcome.BindingNotFound => Results.NotFound(new
            {
                error = "The school and internet line binding was not found."
            }),
            ManualIncidentCreationOutcome.OpenIncidentExists => Results.Conflict(new
            {
                error = "The internet line already has an active incident."
            }),
            _ => throw new InvalidOperationException("Unknown incident creation outcome.")
        };
    })
    .RequireRateLimiting("admin-write");

app.MapPut(
    "/api/incidents/{incidentId:guid}/status",
    async (
        Guid incidentId,
        VkoMonitoring.Api.Models.IncidentStatusChangeRequest statusChange,
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

        statusChange = statusChange with { Actor = MonitoringRequestAccess.From(request.HttpContext)!.Actor };
        var errors = IncidentAdministrationValidator.Validate(statusChange);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var outcome = await repository.ChangeStatusAsync(
            incidentId,
            statusChange,
            cancellationToken);
        return outcome switch
        {
            IncidentStatusChangeOutcome.Updated => Results.NoContent(),
            IncidentStatusChangeOutcome.NotFound => Results.NotFound(),
            IncidentStatusChangeOutcome.InvalidTransition => Results.Conflict(new
            {
                error = "The requested incident status transition is not allowed."
            }),
            _ => throw new InvalidOperationException("Unknown incident status change outcome.")
        };
    })
    .RequireRateLimiting("admin-write");

app.MapPut(
    "/api/incidents/{incidentId:guid}/assignment",
    async (
        Guid incidentId,
        VkoMonitoring.Api.Models.IncidentAssignmentRequest assignment,
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

        assignment = assignment with { Actor = MonitoringRequestAccess.From(request.HttpContext)!.Actor };
        var errors = IncidentAdministrationValidator.Validate(assignment);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return await repository.AssignAsync(incidentId, assignment, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireRateLimiting("admin-write");

app.MapPost(
    "/api/incidents/{incidentId:guid}/comments",
    async (
        Guid incidentId,
        VkoMonitoring.Api.Models.IncidentCommentCreateRequest comment,
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

        comment = comment with { Actor = MonitoringRequestAccess.From(request.HttpContext)!.Actor };
        var errors = IncidentAdministrationValidator.Validate(comment);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        return await repository.AddCommentAsync(incidentId, comment, cancellationToken)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireRateLimiting("admin-write");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("admin-read");

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
    })
    .RequireRateLimiting("speed-test");

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
    })
    .RequireRateLimiting("speed-test");

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

static bool IsValidLifecycleReason(string? reason) =>
    !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 5 and <= 500;
