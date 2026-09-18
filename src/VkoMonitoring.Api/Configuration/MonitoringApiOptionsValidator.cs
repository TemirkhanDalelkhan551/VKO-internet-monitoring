namespace VkoMonitoring.Api.Configuration;

using VkoMonitoring.Api.Security;

public static class MonitoringApiOptionsValidator
{
    public static void Validate(MonitoringApiOptions options, string? postgresConnectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (options.UserSessionLifetimeMinutes is < 5 or > 1440)
            errors.Add("UserSessionLifetimeMinutes must be between 5 and 1440.");
        if (!ConfigurationSecret.IsConfigured(options.AdminToken))
        {
            errors.Add("AdminToken must be configured with a non-placeholder secret.");
        }

        if (options.DeviceTokens.Count == 0 &&
            options.StorageProvider.Equals("Json", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("At least one device token must be configured.");
        }

        foreach (var deviceKey in options.DeviceTokens.Keys)
        {
            if (!Guid.TryParse(deviceKey, out _))
            {
                errors.Add($"Device token key {deviceKey} must be a UUID.");
            }

            if (!options.DeviceBindings.TryGetValue(deviceKey, out var binding))
            {
                errors.Add($"Device binding is missing for {deviceKey}.");
                continue;
            }

            if (binding.SchoolId == Guid.Empty)
            {
                errors.Add($"SchoolId is missing for device {deviceKey}.");
            }

            if (binding.LineId == Guid.Empty)
            {
                errors.Add($"LineId is missing for device {deviceKey}.");
            }
        }

        if (!IsPositive(options.Thresholds.MinimumDownloadMbps) ||
            !IsPositive(options.Thresholds.MinimumUploadMbps) ||
            !IsPositive(options.Thresholds.MaximumPingMilliseconds) ||
            !IsPositive(options.Thresholds.MaximumJitterMilliseconds) ||
            !IsPositive(options.Thresholds.MaximumPacketLossPercent) ||
            !IsPositive(options.Thresholds.MinimumAvailabilityPercent))
        {
            errors.Add("All quality thresholds must be greater than zero.");
        }

        if (options.MaximumMeasurementClockSkewMinutes is < 0 or > 60)
        {
            errors.Add("MaximumMeasurementClockSkewMinutes must be between 0 and 60.");
        }

        if (options.DeviceActiveWindowMinutes <= 0)
        {
            errors.Add("DeviceActiveWindowMinutes must be greater than zero.");
        }

        if (options.MeasurementFreshnessMinutes <= 0)
        {
            errors.Add("MeasurementFreshnessMinutes must be greater than zero.");
        }

        foreach (var binding in options.DeviceBindings.Values)
        {
            if (binding.LineStatus is not ("Primary" or "Backup" or "Disabled"))
            {
                errors.Add("Device binding LineStatus must be Primary, Backup, or Disabled.");
            }
        }

        if (options.Incidents.ConsecutiveProblemMeasurements is < 2 or > 20)
        {
            errors.Add("Incidents:ConsecutiveProblemMeasurements must be between 2 and 20.");
        }

        if (options.Incidents.ConsecutiveRecoveryMeasurements is < 1 or > 20)
        {
            errors.Add("Incidents:ConsecutiveRecoveryMeasurements must be between 1 and 20.");
        }

        if (options.Incidents.MinimumViolationMinutes is < 0 or > 1_440)
        {
            errors.Add("Incidents:MinimumViolationMinutes must be between 0 and 1440.");
        }

        if (options.Incidents.MaximumSignalsToEvaluate is < 20 or > 1_000)
        {
            errors.Add("Incidents:MaximumSignalsToEvaluate must be between 20 and 1000.");
        }

        if (options.ReadinessDegradedAfterMilliseconds <= 0)
        {
            errors.Add("ReadinessDegradedAfterMilliseconds must be greater than zero.");
        }

        if (options.MaximumSpeedTestBytes is < 1_024 or > 100_000_000)
        {
            errors.Add("MaximumSpeedTestBytes must be between 1024 and 100000000 bytes.");
        }

        if (options.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(postgresConnectionString))
            {
                errors.Add("ConnectionStrings:MonitoringDatabase is required for PostgreSql storage.");
            }
        }
        else if (!options.StorageProvider.Equals("Json", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("StorageProvider must be Json or PostgreSql.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static bool IsPositive(decimal value) => value > 0;
}
