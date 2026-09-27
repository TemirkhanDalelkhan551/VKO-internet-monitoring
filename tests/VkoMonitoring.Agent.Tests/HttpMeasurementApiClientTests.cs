using System.Net;
using System.Net.Http.Headers;
using VkoMonitoring.Agent.Core.Abstractions;
using VkoMonitoring.Agent.Core.Domain;
using VkoMonitoring.Agent.Infrastructure.Api;

namespace VkoMonitoring.Agent.Tests;

public sealed class HttpMeasurementApiClientTests
{
    [Fact]
    public async Task SendAsync_ReturnsDeliveredForSuccessfulResponse()
    {
        var client = CreateClient(HttpStatusCode.Created);

        var result = await client.SendAsync(CreateMeasurement(), CancellationToken.None);

        Assert.Equal(MeasurementDeliveryOutcome.Delivered, result.Outcome);
        Assert.Null(result.ResponseStatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task SendAsync_ReturnsPermanentRejectionForInvalidMeasurement(HttpStatusCode statusCode)
    {
        var client = CreateClient(statusCode);

        var result = await client.SendAsync(CreateMeasurement(), CancellationToken.None);

        Assert.Equal(MeasurementDeliveryOutcome.PermanentlyRejected, result.Outcome);
        Assert.Equal((int)statusCode, result.ResponseStatusCode);
    }

    [Fact]
    public async Task SendAsync_ThrowsForTransientServerFailure()
    {
        var client = CreateClient(HttpStatusCode.ServiceUnavailable);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendAsync(CreateMeasurement(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RetriesBadRequestWhenServerProvidesRetryAfter()
    {
        var client = CreateClient(HttpStatusCode.BadRequest, retryAfter: TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SendAsync(CreateMeasurement(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    private static HttpMeasurementApiClient CreateClient(
        HttpStatusCode statusCode,
        TimeSpan? retryAfter = null)
    {
        var httpClient = new HttpClient(new ResponseHandler(statusCode, retryAfter))
        {
            BaseAddress = new Uri("https://monitoring.example/")
        };
        return new HttpMeasurementApiClient(httpClient, new TokenProvider());
    }

    private static InternetMeasurement CreateMeasurement() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        100,
        50,
        20,
        5,
        0,
        ConnectionStatus.Online,
        null,
        "1.0.0");

    private sealed class TokenProvider : IDeviceTokenProvider
    {
        public string GetToken() => "test-device-token";
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode, TimeSpan? retryAfter) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
            }

            return Task.FromResult(response);
        }
    }
}
