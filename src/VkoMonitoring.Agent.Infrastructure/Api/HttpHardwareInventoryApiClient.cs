using System.Net.Http.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class HttpHardwareInventoryApiClient(HttpClient httpClient, IDeviceTokenProvider tokenProvider)
    : IHardwareInventoryApiClient
{
    public async Task SendAsync(HardwareInventory inventory, CancellationToken cancellationToken)
    {
        var path = $"api/devices/{inventory.DeviceId:D}/inventory?schoolId={inventory.SchoolId:D}&lineId={inventory.LineId:D}";
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(inventory) };
        request.Headers.Add("X-Device-Token", tokenProvider.GetToken());
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
