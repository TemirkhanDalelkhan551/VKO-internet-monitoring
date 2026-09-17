namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class ActivationWorkflow(
    SetupPaths paths,
    WindowsAgentService? service = null,
    AgentConfigurationWriter? configurationWriter = null,
    InstallationIdentityProvider? identityProvider = null)
{
    private readonly WindowsAgentService service = service ?? new WindowsAgentService();
    private readonly AgentConfigurationWriter configurationWriter = configurationWriter ?? new AgentConfigurationWriter();
    private readonly InstallationIdentityProvider identityProvider = identityProvider ?? new InstallationIdentityProvider();

    public async Task<AgentActivationResult> ExecuteAsync(
        ActivationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        paths.EnsureAgentIsInstalled();
        service.EnsureInstalled();
        EnsureWritable(paths.ConfigurationPath, paths.DataDirectory);

        var deviceIdentifier = identityProvider.GetOrCreate(paths.DataDirectory);
        using var httpClient = new HttpClient
        {
            BaseAddress = input.ServerAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        var activation = await new ActivationApiClient(httpClient).ActivateAsync(
            new AgentActivationRequest(
                input.ActivationCode,
                deviceIdentifier,
                input.DeviceName,
                input.Room,
                input.ConnectionType),
            cancellationToken);

        var wasRunning = service.IsRunning();
        service.Stop();
        try
        {
            configurationWriter.Save(
                paths.ConfigurationPath,
                paths.DataDirectory,
                input.ServerAddress,
                activation);
            TokenFileAccess.RestrictToSystemAndAdministrators(
                Path.Combine(paths.DataDirectory, "device-token.dat"));
            service.ConfigureAutomaticStart();
            service.Start();
        }
        catch
        {
            if (wasRunning)
            {
                service.Start();
            }

            throw;
        }

        return activation;
    }

    private static void EnsureWritable(string configurationPath, string dataDirectory)
    {
        _ = File.ReadAllText(configurationPath);
        Directory.CreateDirectory(dataDirectory);

        foreach (var directory in new[] { Path.GetDirectoryName(configurationPath), dataDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Не удалось определить каталог конфигурации.");
            }

            var probePath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllText(probePath, string.Empty);
            }
            finally
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
        }
    }
}
