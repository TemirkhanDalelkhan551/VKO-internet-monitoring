using System.Net;
using System.Net.Http.Json;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Setup.Status;

public sealed record LocalStatusView(
    bool IsServiceRunning,
    Uri ServerAddress,
    LocalDeviceStatusResponse ServerStatus);

public sealed class LocalDeviceStatusService(
    LocalAgentSettingsReader settingsReader,
    WindowsAgentService? service = null)
{
    private readonly WindowsAgentService service = service ?? new WindowsAgentService();

    public Uri GetConfiguredServerAddress() => settingsReader.Read().ApiBaseUri;

    public async Task<LocalStatusView> GetAsync(CancellationToken cancellationToken)
    {
        var settings = settingsReader.Read();
        using var client = new HttpClient
        {
            BaseAddress = settings.ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/devices/{settings.DeviceId:D}/status" +
            $"?schoolId={settings.SchoolId:D}&lineId={settings.LineId:D}");
        request.Headers.Add("X-Device-Token", settings.DeviceToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "Сервер отклонил токен этого компьютера. Требуется повторная активация.");
        }

        response.EnsureSuccessStatusCode();
        var serverStatus = await response.Content.ReadFromJsonAsync<LocalDeviceStatusResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("Сервер вернул пустой ответ.");
        return new LocalStatusView(service.IsRunning(), settings.ApiBaseUri, serverStatus);
    }
}
