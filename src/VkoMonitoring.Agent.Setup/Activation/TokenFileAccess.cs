using System.Diagnostics;

namespace VkoMonitoring.Agent.Setup.Activation;

public static class TokenFileAccess
{
    public static void RestrictToSystemAndAdministrators(string tokenPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "icacls.exe",
            Arguments = $"\"{tokenPath}\" /inheritance:r /grant:r *S-1-5-18:F *S-1-5-32-544:F",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Не удалось настроить права файла токена.");

        if (!process.WaitForExit(30_000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException("Не удалось ограничить доступ к файлу токена.");
        }
    }
}
