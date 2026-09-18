using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Api.Validation;

public static class MeasurementValidator
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        InternetMeasurement measurement,
        DateTimeOffset nowUtc,
        TimeSpan maximumClockSkew)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumClockSkew, TimeSpan.Zero);
        var errors = new Dictionary<string, string[]>();

        AddRequiredGuid(measurement.EventId, nameof(measurement.EventId), errors);
        AddRequiredGuid(measurement.SchoolId, nameof(measurement.SchoolId), errors);
        AddRequiredGuid(measurement.DeviceId, nameof(measurement.DeviceId), errors);
        AddRequiredGuid(measurement.LineId, nameof(measurement.LineId), errors);

        if (measurement.MeasuredAtUtc == default)
        {
            errors[nameof(measurement.MeasuredAtUtc)] = ["Measurement timestamp is required."];
        }
        else if (measurement.MeasuredAtUtc > nowUtc + maximumClockSkew)
        {
            errors[nameof(measurement.MeasuredAtUtc)] = ["Measurement timestamp exceeds the allowed clock skew."];
        }

        if (measurement.ConnectionStatus == ConnectionStatus.Online)
        {
            RequireOnlineMetric(measurement.DownloadMbps, nameof(measurement.DownloadMbps), errors);
            RequireOnlineMetric(measurement.UploadMbps, nameof(measurement.UploadMbps), errors);
            RequireOnlineMetric(measurement.PingMilliseconds, nameof(measurement.PingMilliseconds), errors);
            RequireOnlineMetric(measurement.JitterMilliseconds, nameof(measurement.JitterMilliseconds), errors);
            RequireOnlineMetric(measurement.PacketLossPercent, nameof(measurement.PacketLossPercent), errors);
        }

        ValidateNonNegative(measurement.DownloadMbps, nameof(measurement.DownloadMbps), errors);
        ValidateNonNegative(measurement.UploadMbps, nameof(measurement.UploadMbps), errors);
        ValidateNonNegative(measurement.PingMilliseconds, nameof(measurement.PingMilliseconds), errors);
        ValidateNonNegative(measurement.JitterMilliseconds, nameof(measurement.JitterMilliseconds), errors);

        if (measurement.PacketLossPercent is < 0 or > 100 ||
            double.IsNaN(measurement.PacketLossPercent ?? 0) ||
            double.IsInfinity(measurement.PacketLossPercent ?? 0))
        {
            errors[nameof(measurement.PacketLossPercent)] = ["Packet loss must be between 0 and 100 percent."];
        }

        if (measurement.DurationMilliseconds < 0)
        {
            errors[nameof(measurement.DurationMilliseconds)] = ["Duration cannot be negative."];
        }

        if (measurement.MeasurementServer?.Length > 500)
        {
            errors[nameof(measurement.MeasurementServer)] = ["Measurement server is too long."];
        }

        return errors;
    }

    private static void RequireOnlineMetric(
        double? value,
        string propertyName,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[propertyName] = ["Metric is required for an Online measurement."];
        }
    }

    private static void AddRequiredGuid(
        Guid value,
        string propertyName,
        IDictionary<string, string[]> errors)
    {
        if (value == Guid.Empty)
        {
            errors[propertyName] = ["Value is required."];
        }
    }

    private static void ValidateNonNegative(
        double? value,
        string propertyName,
        IDictionary<string, string[]> errors)
    {
        if (value < 0 || double.IsNaN(value ?? 0) || double.IsInfinity(value ?? 0))
        {
            errors[propertyName] = ["Value must be a finite non-negative number."];
        }
    }
}
