namespace VkoMonitoring.Api.Configuration;

public static class MonitoringApiOptionsValidator
{
    public static void Validate(MonitoringApiOptions options, string? postgresConnectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AdminToken))
        {
            errors.Add("AdminToken must be configured.");
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

        if (options.DeviceActiveWindowMinutes <= 0)
        {
            errors.Add("DeviceActiveWindowMinutes must be greater than zero.");
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
