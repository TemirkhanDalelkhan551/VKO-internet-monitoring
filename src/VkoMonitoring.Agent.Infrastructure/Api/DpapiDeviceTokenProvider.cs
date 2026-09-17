using System.Security.Cryptography;
using System.Text;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class DpapiDeviceTokenProvider(AgentOptions options) : IDeviceTokenProvider
{
    private readonly Lazy<string> _token = new(() => ReadToken(options.DeviceTokenFile));

    public string GetToken() => _token.Value;

    private static string ReadToken(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI token storage is available only on Windows.");
        }

        var protectedBytes = Convert.FromBase64String(File.ReadAllText(path).Trim());
        var clearBytes = ProtectedData.Unprotect(
            protectedBytes,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
        var token = Encoding.UTF8.GetString(clearBytes);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("The protected device token is empty.")
            : token;
    }
}
