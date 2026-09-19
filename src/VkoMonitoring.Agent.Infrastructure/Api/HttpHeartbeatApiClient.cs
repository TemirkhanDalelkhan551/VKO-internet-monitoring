using System.Net.Http.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class HttpHeartbeatApiClient(
    HttpClient httpClient,
    IDeviceTokenProvider tokenProvider) : IHeartbeatApiClient
{
    public async Task<AgentRuntimeConfiguration?> SendAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/devices/heartbeat")
        {
            Content = JsonContent.Create(heartbeat)
        };
        request.Headers.Add("X-Device-Token", tokenProvider.GetToken());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var configurationRequest = new HttpRequestMessage(HttpMethod.Get,
            $"api/devices/configuration?schoolId={heartbeat.SchoolId:D}&deviceId={heartbeat.DeviceId:D}&lineId={heartbeat.LineId:D}");
        configurationRequest.Headers.Add("X-Device-Token", tokenProvider.GetToken());
        using var configurationResponse = await httpClient.SendAsync(configurationRequest, cancellationToken);
        configurationResponse.EnsureSuccessStatusCode();
        return await configurationResponse.Content.ReadFromJsonAsync<AgentRuntimeConfiguration>(cancellationToken: cancellationToken);
    }
}
