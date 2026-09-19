using System.Net;
using System.Text;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Tests;

public sealed class ActivationApiClientTests
{
    [Fact]
    public async Task CheckAndPreviewAsync_ChecksReadinessAndReturnsBindingWithoutActivation()
    {
        var schoolId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"Healthy"}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[1024])
            },
            new HttpResponseMessage(HttpStatusCode.NoContent),
            Json(HttpStatusCode.OK, $$"""
                {
                  "schoolId":"{{schoolId}}",
                  "schoolName":"Школа № 1",
                  "lineId":"{{lineId}}",
                  "lineName":"Основная линия",
                  "providerName":"Поставщик",
                  "connectionType":"Fiber",
                  "expiresAtUtc":"2030-01-01T10:00:00+00:00"
                }
                """));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://monitoring.example/") };

        var result = await new ActivationApiClient(client).CheckAndPreviewAsync(
            "ABCD-EFGH-JKLM",
            CancellationToken.None);

        Assert.Equal(schoolId, result.SchoolId);
        Assert.Equal(lineId, result.LineId);
        Assert.Equal("Школа № 1", result.SchoolName);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/health/ready", request.PathAndQuery),
            request => Assert.Equal("/speed/download?bytes=1024", request.PathAndQuery),
            request =>
            {
                Assert.Equal("/speed/upload", request.PathAndQuery);
                Assert.Equal(1024, Convert.FromBase64String(request.Body).Length);
            },
            request =>
            {
                Assert.Equal("/api/devices/activation-preview", request.PathAndQuery);
                Assert.Contains("ABCD-EFGH-JKLM", request.Body, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task CheckAndPreviewAsync_ExplainsServerThatIsNotReady()
    {
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://monitoring.example/") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ActivationApiClient(client).CheckAndPreviewAsync(
                "ABCD-EFGH-JKLM",
                CancellationToken.None));

        Assert.Contains("пока не готов", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAndPreviewAsync_ExplainsRejectedCode()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.OK),
            new HttpResponseMessage(HttpStatusCode.NoContent),
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://monitoring.example/") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ActivationApiClient(client).CheckAndPreviewAsync(
                "ABCD-EFGH-JKLM",
                CancellationToken.None));

        Assert.Contains("просрочен", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupDefaultsReader_UsesConfiguredHttpsAddress()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"vko-setup-defaults-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = Path.Combine(directory, "appsettings.json");
            File.WriteAllText(configuration, """
                {"Setup":{"DefaultApiBaseUrl":"https://monitoring.example.kz/"}}
                """);

            var result = new SetupDefaultsReader(
                new SetupPaths(configuration, Path.Combine(directory, "data"))).GetServerAddress();

            Assert.Equal("https://monitoring.example.kz/", result.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<(string PathAndQuery, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.Headers.ContentType?.MediaType == "application/octet-stream"
                    ? Convert.ToBase64String(await request.Content.ReadAsByteArrayAsync(cancellationToken))
                    : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.PathAndQuery, body));
            return responses.Dequeue();
        }
    }
}
