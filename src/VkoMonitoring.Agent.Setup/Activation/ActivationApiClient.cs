using System.Net;
using System.Net.Http.Json;

namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class ActivationApiClient(HttpClient httpClient)
{
    public async Task<AgentActivationResult> ActivateAsync(
        AgentActivationRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "api/devices/activate",
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Код активации неверен, просрочен или уже использован.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Этот компьютер уже зарегистрирован на сервере.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentActivationResult>(cancellationToken)
            ?? throw new InvalidOperationException("Сервер вернул пустой результат активации.");
    }
}
