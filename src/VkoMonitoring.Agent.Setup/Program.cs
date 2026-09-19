using VkoMonitoring.Agent.Setup.Activation;
using VkoMonitoring.Agent.Setup.Status;
using VkoMonitoring.Agent.Setup.UI;

namespace VkoMonitoring.Agent.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            AdministratorGuard.EnsureElevated();
            if (args.Contains("--install-service", StringComparer.OrdinalIgnoreCase))
            {
                var dataDirectory = SetupPaths.ForInstalledAgent().DataDirectory;
                new WindowsAgentService().InstallOrUpdate(
                    Path.Combine(AppContext.BaseDirectory, "VkoMonitoring.Agent.exe"),
                    File.Exists(Path.Combine(dataDirectory, "device-token.dat")));
                return 0;
            }

            var paths = SetupPaths.ForInstalledAgent();
            paths.EnsureAgentIsInstalled();
            var activationWorkflow = new ActivationWorkflow(paths);
            var settingsReader = new LocalAgentSettingsReader(paths);
            if (settingsReader.IsActivated())
            {
                Application.Run(new DeviceStatusForm(
                    new LocalDeviceStatusService(settingsReader),
                    activationWorkflow,
                    new LocalMeasurementRequestService(paths)));
            }
            else
            {
                Application.Run(new SetupForm(
                    activationWorkflow,
                    new SetupDefaultsReader(paths).GetServerAddress(),
                    new LocalDeviceStatusService(settingsReader)));
            }

            return 0;
        }
        catch (Exception exception)
        {
            if (args.Contains("--install-service", StringComparer.OrdinalIgnoreCase))
            {
                return 1;
            }

            MessageBox.Show(
                exception.Message,
                "Мониторинг интернета ВКО",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
