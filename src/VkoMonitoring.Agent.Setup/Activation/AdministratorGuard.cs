using System.Runtime.Versioning;
using System.Security.Principal;

namespace VkoMonitoring.Agent.Setup.Activation;

public static class AdministratorGuard
{
    [SupportedOSPlatform("windows")]
    public static void EnsureElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException("Запустите мастер настройки от имени администратора.");
        }
    }
}
