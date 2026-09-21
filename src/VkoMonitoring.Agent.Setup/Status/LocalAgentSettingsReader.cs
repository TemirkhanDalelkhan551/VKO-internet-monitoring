using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Setup.Status;

public sealed class LocalAgentSettingsReader(SetupPaths paths)
{
    public bool IsActivated()
    {
        try
        {
            _ = Read();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public LocalAgentSettings Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(paths.ConfigurationPath));
        var agent = document.RootElement.GetProperty("Agent");
        var schoolId = agent.GetProperty("SchoolId").GetGuid();
        var deviceId = agent.GetProperty("DeviceId").GetGuid();
        var lineId = agent.GetProperty("LineId").GetGuid();
        var apiBaseUri = new Uri(agent.GetProperty("ApiBaseUrl").GetString()
            ?? throw new InvalidOperationException("В конфигурации отсутствует адрес сервера."));
        var tokenPath = Environment.ExpandEnvironmentVariables(
            agent.GetProperty("DeviceTokenFile").GetString() ?? string.Empty);
        if (schoolId == Guid.Empty || deviceId == Guid.Empty || lineId == Guid.Empty ||
            string.IsNullOrWhiteSpace(tokenPath) || !File.Exists(tokenPath))
        {
            throw new InvalidOperationException("Компьютер ещё не активирован.");
        }

        var protectedBytes = Convert.FromBase64String(File.ReadAllText(tokenPath).Trim());
        var clearBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        var token = Encoding.UTF8.GetString(clearBytes);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Защищённый токен устройства пуст.");
        }

        var windows = agent.TryGetProperty("MeasurementWindows", out var measurementWindows) &&
                      measurementWindows.ValueKind == JsonValueKind.Array
            ? measurementWindows.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray()
            : [];
        return new LocalAgentSettings(schoolId, deviceId, lineId, apiBaseUri, token)
        {
            MeasurementWindows = windows
        };
    }
}
