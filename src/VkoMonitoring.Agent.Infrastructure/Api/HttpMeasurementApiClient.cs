using System.Net.Http.Json;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Configuration;
using VkoMonitoring.Agent.Core.Domain;

namespace VkoMonitoring.Agent.Infrastructure.Api;

public sealed class HttpMeasurementApiClient(
    HttpClient httpClient,
    IDeviceTokenProvider tokenProvider) : IMeasurementApiClient
{
    public async Task<MeasurementDeliveryResult> SendAsync(
        InternetMeasurement measurement,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/measurements")
        {
            Content = JsonContent.Create(measurement)
        };
        request.Headers.Add("X-Device-Token", tokenProvider.GetToken());

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return MeasurementDeliveryResult.Delivered;
        }

        if ((int)response.StatusCode is 400 or 413 or 422 && response.Headers.RetryAfter is null)
        {
            return MeasurementDeliveryResult.PermanentlyRejected((int)response.StatusCode);
        }

        throw new HttpRequestException(
            $"Monitoring API rejected the measurement with HTTP {(int)response.StatusCode}.",
            inner: null,
            response.StatusCode);
    }
}
