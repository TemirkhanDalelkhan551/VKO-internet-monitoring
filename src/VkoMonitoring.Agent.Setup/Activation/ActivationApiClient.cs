using System.Net;
using System.Net.Http.Json;

namespace VkoMonitoring.Agent.Setup.Activation;

public sealed class ActivationApiClient(HttpClient httpClient)
{
    public async Task<AgentActivationPreviewResult> CheckAndPreviewAsync(
        string activationCode,
        string deviceIdentifier,
        CancellationToken cancellationToken)
    {
        try
        {
            using var readiness = await httpClient.GetAsync("health/ready", cancellationToken);
            if (readiness.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                throw new InvalidOperationException(
                    "Сервер запущен, но пока не готов к работе. Подождите минуту и повторите проверку.");
            }

            readiness.EnsureSuccessStatusCode();
            await CheckMeasurementEndpointsAsync(cancellationToken);
            using var response = await httpClient.PostAsJsonAsync(
                "api/devices/activation-preview",
                new AgentActivationPreviewRequest(activationCode, deviceIdentifier),
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(
                    "Код активации неверен, просрочен или принадлежит другому компьютеру. Для восстановления повторите активацию на исходном компьютере в течение 24 часов либо получите новый код.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new InvalidOperationException(
                    "Слишком много попыток проверки. Подождите минуту и повторите.");
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AgentActivationPreviewResult>(cancellationToken)
                ?? throw new InvalidOperationException("Сервер вернул пустой результат проверки кода.");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "Не удалось подключиться к серверу. Проверьте интернет, адрес сервера, прокси и сертификат HTTPS.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Сервер долго не отвечает. Он может запускаться после простоя. Подождите минуту и повторите проверку.",
                exception);
        }
    }

    private async Task CheckMeasurementEndpointsAsync(CancellationToken cancellationToken)
    {
        using var download = await httpClient.GetAsync(
            "speed/download?bytes=1024",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        download.EnsureSuccessStatusCode();

        using var uploadContent = new ByteArrayContent(new byte[1024]);
        uploadContent.Headers.ContentType = new("application/octet-stream");
        using var upload = await httpClient.PostAsync(
            "speed/upload",
            uploadContent,
            cancellationToken);
        upload.EnsureSuccessStatusCode();
    }

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
            throw new InvalidOperationException(
                "Код активации истёк, отозван или принадлежит другому компьютеру. Повторите на исходном компьютере либо получите новый код.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Этот компьютер уже зарегистрирован на сервере.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "Слишком много попыток активации. Подождите минуту и повторите.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentActivationResult>(cancellationToken)
            ?? throw new InvalidOperationException("Сервер вернул пустой результат активации.");
    }
}
