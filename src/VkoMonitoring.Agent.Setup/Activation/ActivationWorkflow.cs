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

    public async Task<AgentActivationPreviewResult> PreflightAsync(
        ActivationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        PrepareLocalSystem();
        using var httpClient = CreateHttpClient(input.ServerAddress, TimeSpan.FromSeconds(75));
        return await new ActivationApiClient(httpClient).CheckAndPreviewAsync(
            input.ActivationCode,
            cancellationToken);
    }

    public async Task<AgentActivationResult> ExecuteAsync(
        ActivationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        PrepareLocalSystem();

        var deviceIdentifier = identityProvider.GetOrCreate(paths.DataDirectory);
        using var httpClient = CreateHttpClient(input.ServerAddress, TimeSpan.FromSeconds(30));
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

    private void PrepareLocalSystem()
    {
        if (!Environment.Is64BitOperatingSystem || !OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            throw new InvalidOperationException(
                "Для агента требуется 64-битная Windows 10 или Windows 11.");
        }

        paths.EnsureAgentIsInstalled();
        service.EnsureInstalled();
        EnsureWritable(paths.ConfigurationPath, paths.DataDirectory);
    }

    private static HttpClient CreateHttpClient(Uri serverAddress, TimeSpan timeout) => new()
    {
        BaseAddress = serverAddress,
        Timeout = timeout
    };

    private static void EnsureWritable(string configurationPath, string dataDirectory)
    {
        _ = File.ReadAllText(configurationPath);
        Directory.CreateDirectory(dataDirectory);
        var root = Path.GetPathRoot(Path.GetFullPath(dataDirectory));
        if (string.IsNullOrWhiteSpace(root) ||
            new DriveInfo(root).AvailableFreeSpace < 100L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Для установки и журналов требуется не менее 100 МБ свободного места.");
        }

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
