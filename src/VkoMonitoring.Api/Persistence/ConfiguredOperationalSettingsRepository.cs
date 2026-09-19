using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Persistence;

public sealed class ConfiguredOperationalSettingsRepository(MonitoringApiOptions options) : IOperationalSettingsRepository
{
    public Task<OperationalSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(ToSettings(options));
    public Task<OperationalSettings> UpdateAsync(OperationalSettings settings, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Operational settings require PostgreSQL storage.");
    internal static OperationalSettings ToSettings(MonitoringApiOptions options) => new(
        ["08:00-09:00", "12:00-13:00", "16:00-17:00", "20:00-21:00"],
        options.Thresholds.MinimumDownloadMbps, options.Thresholds.MinimumUploadMbps,
        options.Thresholds.MaximumPingMilliseconds, options.Thresholds.MaximumJitterMilliseconds,
        options.Thresholds.MaximumPacketLossPercent, options.Thresholds.MinimumAvailabilityPercent);
}
