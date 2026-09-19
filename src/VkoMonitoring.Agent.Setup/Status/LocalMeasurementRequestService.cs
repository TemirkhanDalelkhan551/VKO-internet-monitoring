using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Setup.Status;

public sealed class LocalMeasurementRequestService(
    SetupPaths paths,
    Func<bool>? isServiceRunning = null)
{
    private readonly Func<bool> isServiceRunning =
        isServiceRunning ?? new WindowsAgentService().IsRunning;

    public async Task RequestAsync(CancellationToken cancellationToken)
    {
        if (!isServiceRunning())
        {
            throw new InvalidOperationException(
                "Служба мониторинга не запущена. Запустите или повторно настройте агент.");
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
