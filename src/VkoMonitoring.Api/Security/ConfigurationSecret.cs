namespace VkoMonitoring.Api.Security;

public static class ConfigurationSecret
{
    public static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
}
