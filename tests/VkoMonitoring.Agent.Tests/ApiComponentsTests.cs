using System.Net;
using Microsoft.AspNetCore.Http;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Health;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Security;
using VkoMonitoring.Api.Services;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Agent.Tests;

public sealed class ApiComponentsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"vko-api-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Repository_AddIfNotExists_IsIdempotentAndReadable()
    {
        var repository = new JsonFileMeasurementRepository(new MonitoringApiOptions
        {
            DataDirectory = _directory
        });
        var measurement = CreateMeasurement();

        var firstAdd = await repository.AddIfNotExistsAsync(measurement, CancellationToken.None);
        var secondAdd = await repository.AddIfNotExistsAsync(measurement, CancellationToken.None);
        var recent = await repository.GetRecentAsync(10, CancellationToken.None);

        Assert.True(firstAdd);
        Assert.False(secondAdd);
        Assert.Single(recent);
        Assert.Equal(measurement, recent[0]);
    }

    [Fact]
    public void TokenValidator_RejectsWrongTokenAndAcceptsConfiguredDevice()
    {
        var deviceId = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var validator = new TokenValidator(new MonitoringApiOptions
        {
            AdminToken = "admin-secret",
            DeviceTokens = new Dictionary<string, string>
            {
                [deviceId.ToString("D")] = "device-secret"
            },
            DeviceBindings = new Dictionary<string, DeviceBindingOptions>
            {
                [deviceId.ToString("D")] = new()
                {
                    SchoolId = schoolId,
                    LineId = lineId
                }
            }
        });

        Assert.True(validator.IsDeviceAuthorized(schoolId, deviceId, lineId, "device-secret"));
        Assert.False(validator.IsDeviceAuthorized(schoolId, deviceId, lineId, "wrong-secret"));
        Assert.False(validator.IsDeviceAuthorized(Guid.NewGuid(), deviceId, lineId, "device-secret"));
        Assert.False(validator.IsDeviceAuthorized(schoolId, deviceId, Guid.NewGuid(), "device-secret"));
        Assert.True(validator.IsAdminAuthorized("admin-secret"));
        Assert.False(validator.IsAdminAuthorized(null));
    }

    [Fact]
    public void OptionsValidator_RequiresBindingForEveryDeviceToken()
    {
        var options = new MonitoringApiOptions
        {
            AdminToken = "admin-secret",
            DeviceTokens = new Dictionary<string, string>
            {
                [Guid.NewGuid().ToString("D")] = "device-secret"
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MonitoringApiOptionsValidator.Validate(options, postgresConnectionString: null));

        Assert.Contains("Device binding is missing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHANGE_ME_ADMIN_TOKEN")]
    public void OptionsValidator_RejectsMissingOrPlaceholderAdminToken(string adminToken)
    {
        var options = new MonitoringApiOptions
        {
            AdminToken = adminToken,
            StorageProvider = "PostgreSql"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MonitoringApiOptionsValidator.Validate(
                options,
                "Host=localhost;Database=monitoring;Username=monitoring;Password=test"));

        Assert.Contains("AdminToken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsValidator_AllowsDatabaseRegisteredDevicesWithoutConfigurationTokens()
    {
        var options = new MonitoringApiOptions
        {
            AdminToken = "admin-secret",
            StorageProvider = "PostgreSql"
        };

        MonitoringApiOptionsValidator.Validate(
            options,
            "Host=localhost;Database=monitoring;Username=monitoring;Password=test");
    }

    [Fact]
    public void PostgresConnectionStringResolver_PreservesNpgsqlConnectionString()
    {
        const string connectionString =
            "Host=localhost;Port=55432;Database=monitoring;Username=monitoring;Password=test";

        Assert.Equal(connectionString, PostgresConnectionStringResolver.Resolve(connectionString));
    }

    [Fact]
    public void PostgresConnectionStringResolver_ConvertsRenderDatabaseUrl()
    {
        var resolved = PostgresConnectionStringResolver.Resolve(
            "postgresql://render_user:p%40ss%3Aword@database.internal:5433/vko_monitoring");
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(resolved);

        Assert.Equal("database.internal", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("vko_monitoring", parsed.Database);
        Assert.Equal("render_user", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal("VkoMonitoring.Api", parsed.ApplicationName);
    }

    [Theory]
    [InlineData("postgresql://database.internal/vko_monitoring")]
    [InlineData("postgresql://user:password@database.internal/")]
    public void PostgresConnectionStringResolver_RejectsIncompleteDatabaseUrl(string databaseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionStringResolver.Resolve(databaseUrl));
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("::ffff:203.0.113.11", "203.0.113.11")]
    public void ClientRateLimitPartition_NormalizesClientAddress(string address, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        Assert.Equal(expected, ClientRateLimitPartition.GetKey(context));
    }

    [Fact]
    public void RequestBodySizePolicy_UsesSeparateApiAndSpeedLimits()
    {
        Assert.Equal(
            RequestBodySizePolicy.MaximumApiRequestBytes,
            RequestBodySizePolicy.GetMaximumBytes("/api/measurements", 5_000_000));
        Assert.Equal(
            5_000_000,
            RequestBodySizePolicy.GetMaximumBytes("/speed/upload", 5_000_000));
    }

    [Fact]
    public void DeviceTokenHasher_IsDeterministicAndDoesNotStorePlainToken()
    {
        const string token = "sensitive-device-token";

        var first = DeviceTokenHasher.Hash(token);
        var second = DeviceTokenHasher.Hash(token);

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.DoesNotContain(token, Convert.ToHexString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceTokenIssuer_GeneratesIndependent256BitTokens()
    {
        var first = DeviceTokenIssuer.Generate();
        var second = DeviceTokenIssuer.Generate();

        Assert.NotEqual(first, second);
        Assert.Equal(32, Convert.FromBase64String(first).Length);
        Assert.Equal(32, Convert.FromBase64String(second).Length);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("CHANGE_ME_BEFORE_INSTALLATION", false)]
    [InlineData("real-device-token", true)]
    public void ConfigurationSecret_RejectsPlaceholderValues(string? value, bool expected)
    {
        Assert.Equal(expected, ConfigurationSecret.IsConfigured(value));
    }

    [Fact]
    public void PostgreSqlSchema_IsEmbeddedAndContainsRequiredEntities()
    {
        const string resourceName = "VkoMonitoring.Api.Persistence.Sql.001_initial_schema.sql";
        using var stream = typeof(PostgresDatabaseInitializer).Assembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        Assert.Contains("CREATE TABLE IF NOT EXISTS schools", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS internet_lines", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS devices", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS measurements", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS device_activation_codes", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS incidents", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS incident_history", sql, StringComparison.Ordinal);
        Assert.Contains("ux_incidents_one_open_per_line", sql, StringComparison.Ordinal);
        Assert.Contains("threshold_download_mbps", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationCodeProtector_GeneratesUserFriendlyCodeAndNormalizesInput()
    {
        var code = ActivationCodeProtector.Generate();

        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", code);
        Assert.True(ActivationCodeProtector.IsValidFormat(code));
        Assert.Equal(
            ActivationCodeProtector.Hash(code),
            ActivationCodeProtector.Hash(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)));
    }

    [Fact]
    public void MeasurementValidator_RejectsInvalidIdentifiersAndMetrics()
    {
        var measurement = CreateMeasurement() with
        {
            DownloadMbps = -1,
            PacketLossPercent = 101
        };
        measurement = measurement with { EventId = Guid.Empty };

        var errors = MeasurementValidator.Validate(measurement, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        Assert.Contains(nameof(measurement.EventId), errors.Keys);
        Assert.Contains(nameof(measurement.DownloadMbps), errors.Keys);
        Assert.Contains(nameof(measurement.PacketLossPercent), errors.Keys);
    }

    [Fact]
    public void StatusEvaluator_UsesConfiguredThresholds()
    {
        var thresholds = new QualityThresholdOptions();
        var normal = CreateMeasurement();
        var unstable = normal with { DownloadMbps = 15 };
        var critical = normal with { DownloadMbps = 5 };
        var offline = normal with { ConnectionStatus = ConnectionStatus.Offline };

        Assert.Equal(MonitoringStatus.Normal, MonitoringStatusEvaluator.Evaluate(normal, thresholds));
        Assert.Equal(MonitoringStatus.Unstable, MonitoringStatusEvaluator.Evaluate(unstable, thresholds));
        Assert.Equal(MonitoringStatus.Critical, MonitoringStatusEvaluator.Evaluate(critical, thresholds));
        Assert.Equal(MonitoringStatus.NoConnection, MonitoringStatusEvaluator.Evaluate(offline, thresholds));
    }

    [Theory]
    [InlineData(false, 1, ApiHealthStatus.Unavailable)]
    [InlineData(true, 100, ApiHealthStatus.Healthy)]
    [InlineData(true, 1_000, ApiHealthStatus.Degraded)]
    public void HealthStatusEvaluator_ReportsDependencyState(
        bool available,
        int durationMilliseconds,
        ApiHealthStatus expected)
    {
        var actual = HealthStatusEvaluator.Evaluate(
            available,
            TimeSpan.FromMilliseconds(durationMilliseconds),
            TimeSpan.FromSeconds(1));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task JsonRepositories_RecordHeartbeatAndExposeActiveDevice()
    {
        var deviceId = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var options = new MonitoringApiOptions
        {
            DataDirectory = _directory,
            DeviceBindings = new Dictionary<string, DeviceBindingOptions>
            {
                [deviceId.ToString("D")] = new()
                {
                    SchoolId = schoolId,
                    LineId = lineId,
                    SchoolName = "School",
                    DeviceName = "Monitoring point"
                }
            }
        };
        var presenceRepository = new JsonFileDevicePresenceRepository(options);
        var heartbeat = new AgentHeartbeat(
            schoolId,
            deviceId,
            lineId,
            DateTimeOffset.UtcNow,
            "1.2.3");

        var recorded = await presenceRepository.RecordHeartbeatAsync(heartbeat, CancellationToken.None);
        var schools = await new JsonFileMonitoringReadRepository(options, TimeProvider.System)
            .GetSchoolsAsync(CancellationToken.None);

        Assert.True(recorded);
        var school = Assert.Single(schools);
        Assert.Equal(1, school.DeviceCount);
        Assert.Equal(1, school.ActiveDeviceCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static InternetMeasurement CreateMeasurement() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        100,
        50,
        20,
        5,
        0,
        ConnectionStatus.Online,
        null,
        "1.0.0");
}
