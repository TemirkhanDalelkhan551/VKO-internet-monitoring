using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Services;

public static class MonitoringStatusEvaluator
{
    public static MonitoringStatus Evaluate(
        InternetMeasurement? measurement,
        QualityThresholdOptions thresholds)
    {
        if (measurement is null)
        {
            return MonitoringStatus.Unknown;
        }

        if (measurement.ConnectionStatus == ConnectionStatus.Offline)
        {
            return MonitoringStatus.NoConnection;
        }

        if (measurement.ConnectionStatus == ConnectionStatus.Degraded ||
            IsCritical(measurement.DownloadMbps, (double)thresholds.MinimumDownloadMbps, lowerIsWorse: true) ||
            IsCritical(measurement.UploadMbps, (double)thresholds.MinimumUploadMbps, lowerIsWorse: true) ||
            IsCritical(measurement.PingMilliseconds, (double)thresholds.MaximumPingMilliseconds, lowerIsWorse: false) ||
            IsCritical(measurement.JitterMilliseconds, (double)thresholds.MaximumJitterMilliseconds, lowerIsWorse: false) ||
            IsCritical(measurement.PacketLossPercent, (double)thresholds.MaximumPacketLossPercent, lowerIsWorse: false))
        {
            return MonitoringStatus.Critical;
        }

        return ViolatesThreshold(measurement.DownloadMbps, (double)thresholds.MinimumDownloadMbps, lowerIsWorse: true) ||
               ViolatesThreshold(measurement.UploadMbps, (double)thresholds.MinimumUploadMbps, lowerIsWorse: true) ||
               ViolatesThreshold(measurement.PingMilliseconds, (double)thresholds.MaximumPingMilliseconds, lowerIsWorse: false) ||
               ViolatesThreshold(measurement.JitterMilliseconds, (double)thresholds.MaximumJitterMilliseconds, lowerIsWorse: false) ||
               ViolatesThreshold(measurement.PacketLossPercent, (double)thresholds.MaximumPacketLossPercent, lowerIsWorse: false)
            ? MonitoringStatus.Unstable
            : MonitoringStatus.Normal;
    }

    private static bool IsCritical(double? value, double threshold, bool lowerIsWorse) =>
        value is not null && (lowerIsWorse
            ? value.Value < threshold * 0.5
            : value.Value > threshold * 2);

    private static bool ViolatesThreshold(double? value, double threshold, bool lowerIsWorse) =>
        value is not null && (lowerIsWorse ? value.Value < threshold : value.Value > threshold);
}
