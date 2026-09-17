using System.Diagnostics;

namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class WindowsAgentService
{
    private const string ServiceName = "VkoInternetMonitoringAgent";

    public bool IsRunning() => HasState("RUNNING");

    public void InstallOrUpdate(string agentExecutablePath, bool isActivated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExecutablePath);
        if (!File.Exists(agentExecutablePath))
        {
            throw new FileNotFoundException("Исполняемый файл агента не найден.", agentExecutablePath);
        }

        var quotedExecutablePath = $"\"{Path.GetFullPath(agentExecutablePath)}\"";
        if (Exists())
        {
            RunCommand("config", ServiceName, "binPath=", quotedExecutablePath, "start=", "demand");
        }
        else
        {
            RunCommand(
                "create",
                ServiceName,
                "binPath=",
                quotedExecutablePath,
                "start=",
                "demand",
                "DisplayName=",
                "VKO Internet Monitoring Agent");
        }

        RunCommand(
            "description",
            ServiceName,
            "Автономный мониторинг качества интернет-соединения");
        RunCommand(
            "failure",
            ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/60000/restart/60000/restart/60000");

        if (isActivated)
        {
            ConfigureAutomaticStart();
            Start();
        }
    }

    public void Stop()
    {
        if (!HasState("STOPPED"))
        {
            RunAndWait("stop", "STOPPED");
        }
    }

    public void Start()
    {
        if (!HasState("RUNNING"))
        {
            RunAndWait("start", "RUNNING");
        }
    }

    public void ConfigureAutomaticStart()
    {
        RunCommand("config", ServiceName, "start=", "auto");
    }

    public void EnsureInstalled() => _ = QueryState();

    private static bool Exists()
    {
        using var process = StartProcess(["query", ServiceName], redirectOutput: true);
        _ = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private static void RunAndWait(string action, string expectedState)
    {
        using var process = StartProcess([action, ServiceName]);
        if (!process.WaitForExit(30_000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось выполнить команду службы: {action}.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (HasState(expectedState))
            {
                return;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException($"Служба не перешла в состояние {expectedState}.");
    }

    private static bool HasState(string expectedState) =>
        QueryState().Contains(expectedState, StringComparison.Ordinal);

    private static string QueryState()
    {
        using var process = StartProcess(["query", ServiceName], redirectOutput: true);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Служба агента не установлена.");
        }

        return output;
    }

    private static void RunCommand(params string[] arguments)
    {
        using var process = StartProcess(arguments);
        if (!process.WaitForExit(30_000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Команда управления службой завершилась с ошибкой: {arguments[0]}.");
        }
    }

    private static Process StartProcess(IEnumerable<string> arguments, bool redirectOutput = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = redirectOutput,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить диспетчер служб Windows.");
    }
}
