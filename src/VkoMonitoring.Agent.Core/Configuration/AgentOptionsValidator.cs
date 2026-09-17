namespace VkoMonitoring.Agent.Core.Configuration;

public static class AgentOptionsValidator
{
    public static void Validate(AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        AddIfEmpty(options.SchoolId, nameof(options.SchoolId), errors);
        AddIfEmpty(options.DeviceId, nameof(options.DeviceId), errors);
        AddIfEmpty(options.LineId, nameof(options.LineId), errors);

        if (!IsSecureOrLoopbackUrl(options.ApiBaseUrl))
        {
            errors.Add("ApiBaseUrl must use HTTPS. HTTP is allowed only for loopback development URLs.");
        }

        if (!IsSecureOrLoopbackUrl(options.DownloadTestUrl))
        {
            errors.Add("DownloadTestUrl must use HTTPS. HTTP is allowed only for loopback development URLs.");
        }

        if (!IsSecureOrLoopbackUrl(options.UploadTestUrl))
        {
            errors.Add("UploadTestUrl must use HTTPS. HTTP is allowed only for loopback development URLs.");
        }

        if (string.IsNullOrWhiteSpace(options.PingHost))
        {
            errors.Add("PingHost is required.");
        }

        var hasPlainToken = !string.IsNullOrWhiteSpace(options.DeviceToken);
        var hasProtectedToken = !string.IsNullOrWhiteSpace(options.DeviceTokenFile);
        if (hasPlainToken == hasProtectedToken)
        {
            errors.Add("Configure exactly one of DeviceToken or DeviceTokenFile.");
        }

        if (hasProtectedToken && !Path.IsPathFullyQualified(options.DeviceTokenFile))
        {
            errors.Add("DeviceTokenFile must be an absolute path.");
        }

        if (hasPlainToken && !IsLoopbackUrl(options.ApiBaseUrl))
        {
            errors.Add("Plain DeviceToken is allowed only for loopback development. Use DeviceTokenFile for remote servers.");
        }

        AddIfNotPositive(options.DownloadBytes, nameof(options.DownloadBytes), errors);
        AddIfNotPositive(options.UploadBytes, nameof(options.UploadBytes), errors);
        AddIfNotPositive(options.PingAttempts, nameof(options.PingAttempts), errors);
        AddIfNotPositive(options.PingTimeoutMilliseconds, nameof(options.PingTimeoutMilliseconds), errors);
        AddIfNotPositive(options.DispatchIntervalSeconds, nameof(options.DispatchIntervalSeconds), errors);
        AddIfNotPositive(options.HeartbeatIntervalSeconds, nameof(options.HeartbeatIntervalSeconds), errors);
        AddIfNotPositive(options.MaxQueuedMeasurements, nameof(options.MaxQueuedMeasurements), errors);
        AddIfNotPositive(options.DiagnosticLogMaxFileSizeBytes, nameof(options.DiagnosticLogMaxFileSizeBytes), errors);
        AddIfNotPositive(options.DiagnosticLogRetentionDays, nameof(options.DiagnosticLogRetentionDays), errors);
        AddPercentage(options.MaximumCpuUsagePercent, nameof(options.MaximumCpuUsagePercent), errors);
        AddIfNotPositive(options.SystemLoadSampleMilliseconds, nameof(options.SystemLoadSampleMilliseconds), errors);
        AddIfNotPositive(options.SystemLoadRetrySeconds, nameof(options.SystemLoadRetrySeconds), errors);

        if (options.MaximumBackgroundNetworkMbps < 0 ||
            double.IsNaN(options.MaximumBackgroundNetworkMbps) ||
            double.IsInfinity(options.MaximumBackgroundNetworkMbps))
        {
            errors.Add("MaximumBackgroundNetworkMbps must be a finite non-negative number.");
        }

        if (options.MaximumLoadDeferralSeconds < 0)
        {
            errors.Add("MaximumLoadDeferralSeconds cannot be negative.");
        }

        if (options.MaxDispatchIntervalSeconds < options.DispatchIntervalSeconds)
        {
            errors.Add("MaxDispatchIntervalSeconds must be greater than or equal to DispatchIntervalSeconds.");
        }

        if (options.DispatchJitterSeconds < 0)
        {
            errors.Add("DispatchJitterSeconds cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            errors.Add("DataDirectory is required.");
        }

        if (options.MeasurementWindows.Length is < 3 or > 5)
        {
            errors.Add("MeasurementWindows must contain between 3 and 5 daily windows.");
        }

        foreach (var window in options.MeasurementWindows)
        {
            _ = DailyWindow.Parse(window);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static bool IsSecureOrLoopbackUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    private static bool IsLoopbackUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static void AddIfEmpty(Guid value, string name, ICollection<string> errors)
    {
        if (value == Guid.Empty)
        {
            errors.Add($"{name} is required.");
        }
    }

    private static void AddIfNotPositive(int value, string name, ICollection<string> errors)
    {
        if (value <= 0)
        {
            errors.Add($"{name} must be greater than zero.");
        }
    }

    private static void AddPercentage(double value, string name, ICollection<string> errors)
    {
        if (value is <= 0 or > 100 || double.IsNaN(value) || double.IsInfinity(value))
        {
            errors.Add($"{name} must be between 0 and 100.");
        }
    }
}
