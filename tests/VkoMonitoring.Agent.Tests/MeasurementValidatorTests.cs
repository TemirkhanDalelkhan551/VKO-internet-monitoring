using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Validation;

namespace VkoMonitoring.Agent.Tests;

public sealed class MeasurementValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(nameof(InternetMeasurement.DownloadMbps))]
    [InlineData(nameof(InternetMeasurement.UploadMbps))]
    [InlineData(nameof(InternetMeasurement.PingMilliseconds))]
    [InlineData(nameof(InternetMeasurement.JitterMilliseconds))]
    [InlineData(nameof(InternetMeasurement.PacketLossPercent))]
    public void Online_RejectsEachMissingMetric(string metric)
    {
        var measurement = metric switch
        {
            nameof(InternetMeasurement.DownloadMbps) => Create() with { DownloadMbps = null },
            nameof(InternetMeasurement.UploadMbps) => Create() with { UploadMbps = null },
            nameof(InternetMeasurement.PingMilliseconds) => Create() with { PingMilliseconds = null },
            nameof(InternetMeasurement.JitterMilliseconds) => Create() with { JitterMilliseconds = null },
            _ => Create() with { PacketLossPercent = null }
        };

        Assert.Contains(metric, Validate(measurement).Keys);
    }

    [Fact]
    public void Online_RejectsAllMissingMetrics()
    {
        Assert.Equal(5, Validate(WithoutMetrics(ConnectionStatus.Online)).Count);
    }

    [Theory]
    [InlineData(ConnectionStatus.Offline)]
    [InlineData(ConnectionStatus.Degraded)]
    public void DiagnosticResult_AcceptsMissingMetrics(ConnectionStatus status)
    {
        Assert.Empty(Validate(WithoutMetrics(status)));
    }

    [Fact]
    public void Online_AcceptsCompleteMetricsBelowQualityThreshold()
    {
        Assert.Empty(Validate(Create() with { DownloadMbps = 10, UploadMbps = 0 }));
    }

    [Theory]
    [InlineData(-525600, true)]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    [InlineData(864000, false)]
    public void Timestamp_EnforcesFutureBoundaryAndAllowsOfflineHistory(int seconds, bool accepted)
    {
        var errors = Validate(Create() with { MeasuredAtUtc = Now.AddSeconds(seconds) });

        Assert.Equal(accepted, errors.Count == 0);
        if (!accepted)
        {
            Assert.Contains(nameof(InternetMeasurement.MeasuredAtUtc), errors.Keys);
        }
    }

    [Fact]
    public void Timestamp_ComparesInstantsAcrossTimeZones()
    {
        Assert.Empty(Validate(Create() with { MeasuredAtUtc = Now.ToOffset(TimeSpan.FromHours(5)) }));
    }

    [Fact]
    public void Timestamp_UsesConfiguredTolerance()
    {
        var measurement = Create() with { MeasuredAtUtc = Now.AddMinutes(2) };

        Assert.NotEmpty(MeasurementValidator.Validate(measurement, Now, TimeSpan.FromMinutes(1)));
        Assert.Empty(MeasurementValidator.Validate(measurement, Now, TimeSpan.FromMinutes(2)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PacketLoss_RejectsNonFiniteValues(double packetLoss)
    {
        Assert.Contains(nameof(InternetMeasurement.PacketLossPercent),
            Validate(Create() with { PacketLossPercent = packetLoss }).Keys);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void Options_RejectInvalidClockSkew(int minutes)
    {
        var options = new MonitoringApiOptions
        {
            AdminToken = "test-admin-token",
            StorageProvider = "PostgreSql",
            MaximumMeasurementClockSkewMinutes = minutes
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MonitoringApiOptionsValidator.Validate(options, "Host=localhost;Database=test"));

        Assert.Contains(nameof(options.MaximumMeasurementClockSkewMinutes), exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string[]> Validate(InternetMeasurement measurement) =>
        MeasurementValidator.Validate(measurement, Now, ClockSkew);

    private static InternetMeasurement WithoutMetrics(ConnectionStatus status) =>
        Create() with
        {
            ConnectionStatus = status,
            DownloadMbps = null,
            UploadMbps = null,
            PingMilliseconds = null,
            JitterMilliseconds = null,
            PacketLossPercent = null
        };

    private static InternetMeasurement Create() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now,
        50, 50, 20, 1, 0, ConnectionStatus.Online, null, "test");
}
