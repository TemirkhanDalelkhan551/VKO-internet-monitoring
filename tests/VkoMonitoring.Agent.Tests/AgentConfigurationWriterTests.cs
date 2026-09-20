using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;
using VkoMonitoring.Agent.Setup.Activation;
using VkoMonitoring.Agent.Setup.Status;

namespace VkoMonitoring.Agent.Tests;

public sealed class AgentConfigurationWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"vko-activation-tests-{Guid.NewGuid():N}");

    [Fact]
    [SupportedOSPlatform("windows")]
    public void InstallationIdentityProvider_ReusesIdentifierAcrossRuns()
    {
        var provider = new InstallationIdentityProvider();

        var first = provider.GetOrCreate(_directory);
        var second = provider.GetOrCreate(_directory);

        Assert.Equal(first, second);
        Assert.StartsWith(Environment.MachineName + "-", first, StringComparison.Ordinal);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Save_ProtectsTokenAndUpdatesAgentBinding()
    {
        Directory.CreateDirectory(_directory);
        var configurationPath = Path.Combine(_directory, "appsettings.json");
        var dataDirectory = Path.Combine(_directory, "data");
        File.WriteAllText(configurationPath, """
            { "Agent": { "DeviceToken": "old", "PingHost": "1.1.1.1" } }
            """);
        var activation = new AgentActivationResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "device-id",
            "new-secret-token",
            false);

        new AgentConfigurationWriter().Save(
            configurationPath,
            dataDirectory,
            new Uri("http://localhost:5080"),
            activation);

        var agent = JsonNode.Parse(File.ReadAllText(configurationPath))!["Agent"]!.AsObject();
        var tokenPath = agent["DeviceTokenFile"]!.GetValue<string>();
        var protectedToken = Convert.FromBase64String(File.ReadAllText(tokenPath));
        var clearToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(
            protectedToken,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine));

        Assert.Equal(activation.DeviceId, agent["DeviceId"]!.GetValue<Guid>());
        Assert.Equal(string.Empty, agent["DeviceToken"]!.GetValue<string>());
        Assert.Equal("http://localhost:5080/", agent["ApiBaseUrl"]!.GetValue<string>());
        Assert.Equal("new-secret-token", clearToken);
        Assert.Equal("1.1.1.1", agent["PingHost"]!.GetValue<string>());
        Assert.Equal(
            "http://localhost:5080/",
            JsonNode.Parse(File.ReadAllText(configurationPath))!["Setup"]!["DefaultApiBaseUrl"]!.GetValue<string>());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Save_RestoresPreviousTokenWhenConfigurationCommitFails()
    {
        Directory.CreateDirectory(_directory);
        var configurationPath = Path.Combine(_directory, "appsettings.json");
        var dataDirectory = Path.Combine(_directory, "data");
        Directory.CreateDirectory(dataDirectory);
        var originalConfiguration = "{ \"Agent\": { \"DeviceToken\": \"\" } }";
        var tokenPath = Path.Combine(dataDirectory, "device-token.dat");
        File.WriteAllText(configurationPath, originalConfiguration);
        File.WriteAllText(tokenPath, "previous-protected-token");
        File.SetAttributes(configurationPath, FileAttributes.ReadOnly);
        try
        {
            var activation = new AgentActivationResult(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "device-id", "new-token", false);

            Assert.Throws<UnauthorizedAccessException>(() => new AgentConfigurationWriter().Save(
                configurationPath, dataDirectory, new Uri("https://monitoring.example.kz"), activation));

            Assert.Equal("previous-protected-token", File.ReadAllText(tokenPath));
            Assert.Equal(originalConfiguration, File.ReadAllText(configurationPath));
        }
        finally
        {
            File.SetAttributes(configurationPath, FileAttributes.Normal);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void LocalAgentSettingsReader_ReadsOnlyProtectedLocalBinding()
    {
        Directory.CreateDirectory(_directory);
        var configurationPath = Path.Combine(_directory, "appsettings.json");
        var dataDirectory = Path.Combine(_directory, "data");
        File.WriteAllText(configurationPath, """
            { "Agent": { "DeviceToken": "", "PingHost": "1.1.1.1" } }
            """);
        var activation = new AgentActivationResult(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "local-device",
            "protected-local-token",
            false);
        new AgentConfigurationWriter().Save(
            configurationPath,
            dataDirectory,
            new Uri("https://monitoring.example.kz"),
            activation);

        var settings = new LocalAgentSettingsReader(
            new SetupPaths(configurationPath, dataDirectory)).Read();

        Assert.Equal(activation.SchoolId, settings.SchoolId);
        Assert.Equal(activation.DeviceId, settings.DeviceId);
        Assert.Equal(activation.LineId, settings.LineId);
        Assert.Equal("https://monitoring.example.kz/", settings.ApiBaseUri.AbsoluteUri);
        Assert.Equal("protected-local-token", settings.DeviceToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
