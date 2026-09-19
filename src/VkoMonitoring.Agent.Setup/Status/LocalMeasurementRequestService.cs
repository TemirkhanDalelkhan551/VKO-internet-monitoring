using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Setup.Status;

public sealed class LocalMeasurementRequestService(
    SetupPaths paths,
    Func<bool>? isServiceRunning = null,
    Action? startService = null)
{
    private readonly WindowsAgentService defaultWindowsService = new();
    private readonly Func<bool>? configuredIsServiceRunning = isServiceRunning;
    private readonly Action? configuredStartService = startService;

    public async Task<LocalMeasurementRequestResult> RequestAsync(
        CancellationToken cancellationToken)
    {
        var isRunning = configuredIsServiceRunning ?? defaultWindowsService.IsRunning;
        var start = configuredStartService ?? defaultWindowsService.Start;
        var serviceWasStarted = false;
        if (!isRunning())
        {
            try
            {
                start();
                serviceWasStarted = true;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Служба мониторинга остановлена, и автоматически запустить её не удалось. " +
                    "Откройте журнал работы или повторно запустите установщик.",
                    exception);
            }

            if (!isRunning())
            {
                throw new InvalidOperationException(
                    "Служба мониторинга была запущена, но сразу остановилась. " +
                    "Откройте журнал работы для диагностики.");
            }
        }

        Directory.CreateDirectory(paths.DataDirectory);
        var requestPath = Path.Combine(paths.DataDirectory, MeasurementTriggerFile.Name);
        var temporaryPath = requestPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken);
            File.Move(temporaryPath, requestPath, overwrite: true);
            return new LocalMeasurementRequestResult(serviceWasStarted);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

public sealed record LocalMeasurementRequestResult(bool ServiceWasStarted);
