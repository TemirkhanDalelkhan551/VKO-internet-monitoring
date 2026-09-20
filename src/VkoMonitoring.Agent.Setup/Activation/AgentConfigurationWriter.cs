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

        var originalConfiguration = File.ReadAllBytes(configurationPath);
        var root = JsonNode.Parse(Encoding.UTF8.GetString(originalConfiguration))?.AsObject()
            ?? throw new InvalidOperationException("Файл конфигурации имеет неверный формат JSON.");
        var agent = root["Agent"]?.AsObject()
            ?? throw new InvalidOperationException("В конфигурации отсутствует раздел Agent.");

        Directory.CreateDirectory(dataDirectory);
        var tokenPath = Path.Combine(dataDirectory, "device-token.dat");
        var originalToken = File.Exists(tokenPath) ? File.ReadAllBytes(tokenPath) : null;

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

        var configurationTemporaryPath = configurationPath + $".{Guid.NewGuid():N}.tmp";
        var tokenTemporaryPath = tokenPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                configurationTemporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(
                tokenTemporaryPath,
                ProtectToken(activation.DeviceToken),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tokenTemporaryPath, tokenPath, overwrite: true);
            File.Move(configurationTemporaryPath, configurationPath, overwrite: true);
        }
        catch
        {
            RestoreFile(tokenPath, originalToken);
            RestoreFile(configurationPath, originalConfiguration);
            throw;
        }
        finally
        {
            DeleteIfPresent(configurationTemporaryPath);
            DeleteIfPresent(tokenTemporaryPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ProtectToken(string token)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    private static void RestoreFile(string path, byte[]? contents)
    {
        if (contents is null) DeleteIfPresent(path);
        else if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(contents))
            File.WriteAllBytes(path, contents);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + '/', UriKind.Absolute);
}
