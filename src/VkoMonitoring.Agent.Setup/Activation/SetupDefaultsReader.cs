using System.Text.Json;

namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class SetupDefaultsReader(SetupPaths paths)
{
    private static readonly Uri FallbackServerAddress =
        new("https://vko-internet-monitoring-api.onrender.com/");

    public Uri GetServerAddress()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paths.ConfigurationPath));
            if (document.RootElement.TryGetProperty("Setup", out var setup) &&
                setup.TryGetProperty("DefaultApiBaseUrl", out var value) &&
                Uri.TryCreate(value.GetString(), UriKind.Absolute, out var configured) &&
                configured.Scheme == Uri.UriSchemeHttps)
            {
                return configured;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return FallbackServerAddress;
    }
}
