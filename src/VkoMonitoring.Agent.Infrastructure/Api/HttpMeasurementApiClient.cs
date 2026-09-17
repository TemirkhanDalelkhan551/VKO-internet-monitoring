using System.Net.Http.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class HttpMeasurementApiClient(
    HttpClient httpClient,
    IDeviceTokenProvider tokenProvider) : IMeasurementApiClient
{
    public async Task SendAsync(InternetMeasurement measurement, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/measurements")
        {
            Content = JsonContent.Create(measurement)
        };
        request.Headers.Add("X-Device-Token", tokenProvider.GetToken());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
