namespace VkoMonitoring.Agent.Setup.Activation;

public sealed record SetupPaths(string ConfigurationPath, string DataDirectory)
{
    public static SetupPaths ForInstalledAgent()
    {
        var installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "VkoInternetMonitoringAgent");
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VkoInternetMonitoringAgent");

        return new SetupPaths(
            Path.Combine(installDirectory, "appsettings.json"),
            dataDirectory);
    }

    public void EnsureAgentIsInstalled()
    {
        if (!File.Exists(ConfigurationPath))
        {
            throw new InvalidOperationException(
                "Агент не установлен. Сначала запустите установщик агента мониторинга.");
        }
    }
}
