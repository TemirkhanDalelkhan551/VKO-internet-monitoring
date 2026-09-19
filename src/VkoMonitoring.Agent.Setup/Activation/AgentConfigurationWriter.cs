using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class AgentConfigurationWriter
{
    public void Save(
        string configurationPath,
        string dataDirectory,
        Uri apiBaseUri,
        AgentActivationResult activation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        ArgumentNullException.ThrowIfNull(activation);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Защита токена DPAPI доступна только в Windows.");
        }

        var root = JsonNode.Parse(File.ReadAllText(configurationPath))?.AsObject()
            ?? throw new InvalidOperationException("Файл конфигурации имеет неверный формат JSON.");
        var agent = root["Agent"]?.AsObject()
            ?? throw new InvalidOperationException("В конфигурации отсутствует раздел Agent.");

        Directory.CreateDirectory(dataDirectory);
        var tokenPath = Path.Combine(dataDirectory, "device-token.dat");
        WriteProtectedToken(tokenPath, activation.DeviceToken);

        var normalizedBaseUri = EnsureTrailingSlash(apiBaseUri);
        agent["SchoolId"] = activation.SchoolId;
        agent["LineId"] = activation.LineId;
        agent["DeviceId"] = activation.DeviceId;
        agent["DeviceToken"] = string.Empty;
        agent["DeviceTokenFile"] = tokenPath;
        agent["ApiBaseUrl"] = normalizedBaseUri.AbsoluteUri;
        agent["DownloadTestUrl"] = new Uri(normalizedBaseUri, "speed/download").AbsoluteUri;
        agent["UploadTestUrl"] = new Uri(normalizedBaseUri, "speed/upload").AbsoluteUri;
        agent["DataDirectory"] = dataDirectory;
        var setup = root["Setup"] as JsonObject ?? new JsonObject();
        setup["DefaultApiBaseUrl"] = normalizedBaseUri.AbsoluteUri;
        root["Setup"] = setup;

        var temporaryPath = configurationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteProtectedToken(string path, string token)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                Convert.ToBase64String(protectedBytes),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + '/', UriKind.Absolute);
}
