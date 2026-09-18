using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class MonitoringOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoringApiOptions Options = new() { MeasurementFreshnessMinutes = 60 };

    [Theory]
    [InlineData(59, MeasurementFreshness.Fresh, MonitoringStatus.Normal)]
    [InlineData(60, MeasurementFreshness.Fresh, MonitoringStatus.Normal)]
    [InlineData(61, MeasurementFreshness.Stale, MonitoringStatus.Unknown)]
    public void OldMeasurementDoesNotAdvertiseCurrentQuality(int age, MeasurementFreshness freshness, MonitoringStatus status)
    {
        var result = MonitoringOverviewFactory.WithCurrentState(Line("Primary", age), Now, Options);
        Assert.Equal(freshness, result.MeasurementFreshness);
        Assert.Equal(status, result.Status);
        Assert.Equal(MonitoringStatus.Normal, result.QualityStatus);
    }

    [Fact]
    public void MissingMeasurementIsDistinctFromStale()
    {
        Assert.Equal(MeasurementFreshness.Missing, MonitoringOverviewFactory.GetFreshness(null, Now, Options));
    }

    [Theory]
    [InlineData(15, false, AgentPresence.Active)]
    [InlineData(16, false, AgentPresence.Inactive)]
    [InlineData(0, true, AgentPresence.Blocked)]
    public void PresenceUsesItsOwnWindow(int age, bool blocked, AgentPresence expected)
    {
        Assert.Equal(expected, MonitoringOverviewFactory.GetPresence(Now.AddMinutes(-age), blocked, Now, Options));
    }

    [Fact]
    public void MissingHeartbeatDoesNotMeanInternetFailure()
    {
        var result = MonitoringOverviewFactory.WithCurrentState(Line("Primary", 1) with { LastSeenAtUtc = null }, Now, Options);
        Assert.Equal(AgentPresence.NotSeen, result.AgentPresence);
        Assert.Equal(MonitoringStatus.Normal, result.Status);
    }

    [Fact]
    public void DisabledLineKeepsHistoricalQualityButHasUnknownCurrentStatus()
    {
        var result = MonitoringOverviewFactory.WithCurrentState(Line("Disabled", 1), Now, Options);
        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Equal(MonitoringStatus.Normal, result.QualityStatus);
    }

    [Fact]
    public void BlockedDeviceIsNotPresentedAsHealthy()
    {
        var result = MonitoringOverviewFactory.WithCurrentState(new DeviceOverview(
            Guid.NewGuid(), Guid.NewGuid(), "device", "Device", null, null, Now, "test", true,
            MonitoringStatus.Normal, Snapshot(1)), Now, Options);
        Assert.Equal(AgentPresence.Blocked, result.AgentPresence);
        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Equal(MonitoringStatus.Normal, result.QualityStatus);
    }

    [Fact]
    public void FreshBackupCannotReplaceStalePrimaryOrItsProvider()
    {
        var primary = MonitoringOverviewFactory.WithCurrentState(Line("Primary", 61) with { ProviderName = "Main provider" }, Now, Options);
        var backup = MonitoringOverviewFactory.WithCurrentState(Line("Backup", 1) with { ProviderName = "Backup provider" }, Now, Options);
        var result = MonitoringOverviewFactory.WithLines(School(), [backup, primary]);
        Assert.Equal(primary.LineId, result.PrimaryLineId);
        Assert.Equal("Main provider", result.ProviderName);
        Assert.Equal(primary.LatestMeasurement, result.LatestMeasurement);
        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Equal(MeasurementFreshness.Stale, result.MeasurementFreshness);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(MonitoringStatus.Normal, backup.Status);
    }

    [Fact]
    public void BackupOnlySchoolDoesNotPretendToHavePrimary()
    {
        var result = MonitoringOverviewFactory.WithLines(School(), [Line("Backup", 1)]);
        Assert.Null(result.PrimaryLineId);
        Assert.Null(result.LatestMeasurement);
        Assert.Null(result.ProviderName);
        Assert.Equal(MonitoringStatus.Unknown, result.Status);
        Assert.Equal(MeasurementFreshness.Missing, result.MeasurementFreshness);
    }

    private static MeasurementSnapshot Snapshot(int age) => new(50, 50, 20, 1, 0, Now.AddMinutes(-age));
    private static LineOverview Line(string type, int age) => new(Guid.NewGuid(), Guid.NewGuid(), type, type,
        "Provider", "Ethernet", 50, 50, 1, 1, Now, MonitoringStatus.Normal, Snapshot(age));
    private static SchoolOverview School() => new(Guid.NewGuid(), "School", null, null, null, null,
        null, null, 2, 2, MonitoringStatus.Unknown, null);
}
