using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;

namespace VkoMonitoring.Api.Services;

public interface ITelegramNotificationDispatcher
{
    bool TryEnqueue(IncidentNotificationEvent notification);
    bool TryEnqueueTest();
}

/// <summary>
/// Sends only state transitions, never individual measurements. The bounded in-memory queue keeps
/// Telegram latency and outages from delaying the agent write path.
/// </summary>
public sealed class TelegramNotificationDispatcher(
    IHttpClientFactory httpClientFactory,
    IIncidentRepository incidents,
    TelegramNotificationOptions options,
    ILogger<TelegramNotificationDispatcher> logger) : BackgroundService, ITelegramNotificationDispatcher
{
    private readonly Channel<TelegramNotificationJob> _queue = Channel.CreateBounded<TelegramNotificationJob>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    public bool TryEnqueue(IncidentNotificationEvent notification)
    {
        if (!IsConfigured()) return true;
        if (_queue.Writer.TryWrite(new TelegramNotificationJob(notification, false))) return true;
        logger.LogWarning("Telegram notification queue is full; incident {IncidentId} was not queued.", notification.IncidentId);
        return false;
    }

    public bool TryEnqueueTest()
    {
        if (!IsConfigured())
        {
            logger.LogWarning("Telegram test notification was requested, but Telegram is not configured.");
            return false;
        }

        if (_queue.Writer.TryWrite(new TelegramNotificationJob(null, true))) return true;
        logger.LogWarning("Telegram notification queue is full; the test notification was not queued.");
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (job.IsTest)
                {
                    await DeliverTestAsync(stoppingToken);
                    continue;
                }

                var notification = job.IncidentNotification!;
                var incident = await incidents.GetIncidentAsync(notification.IncidentId, stoppingToken);
                if (incident is not null) await DeliverAsync(incident.Incident, notification.Kind, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram notification {NotificationKind} was not delivered for incident {IncidentId}.",
                    job.IsTest ? "test" : "incident", job.IncidentNotification?.IncidentId);
            }
        }
    }

    private async Task DeliverAsync(IncidentOverview incident, IncidentNotificationKind kind, CancellationToken cancellationToken)
    {
        var emoji = kind == IncidentNotificationKind.Opened ? "🔴" : "🟢";
        var heading = kind == IncidentNotificationKind.Opened ? "Новый инцидент" : "Связь восстановлена";
        var occurred = kind == IncidentNotificationKind.Opened ? incident.DetectedAtUtc : incident.RecoveredAtUtc ?? incident.ClosedAtUtc ?? incident.DetectedAtUtc;
        var text = $"{emoji} {heading}\n\n" +
            $"Школа: {incident.SchoolName}\n" +
            $"Линия: {incident.LineName}\n" +
            $"Инцидент: {incident.IncidentNumber}\n" +
            $"Причина: {incident.Description}\n" +
            $"Время: {occurred:dd.MM.yyyy HH:mm} UTC\n\n" +
            $"Подробнее: {options.PublicAppUrl.TrimEnd('/')}";

        using var client = httpClientFactory.CreateClient("telegram-notifications");
        foreach (var chatId in options.ChatIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"bot{options.BotToken}/sendMessage")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId.Trim(), text }), Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Telegram rejected notification for incident {IncidentId}: HTTP {StatusCode}.", incident.IncidentId, (int)response.StatusCode);
        }
    }

    private async Task DeliverTestAsync(CancellationToken cancellationToken)
    {
        const string text = "✅ Тест Telegram-уведомлений\n\n" +
            "Настройка Render активна. Это проверочное сообщение не создаёт замер, инцидент или изменение статистики.";
        await DeliverTextAsync(text, cancellationToken);
    }

    private async Task DeliverTextAsync(string text, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("telegram-notifications");
        foreach (var chatId in options.ChatIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"bot{options.BotToken}/sendMessage")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId.Trim(), text }), Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Telegram rejected a notification test: HTTP {StatusCode}.", (int)response.StatusCode);
        }
    }

    private bool IsConfigured() => options.Enabled && !string.IsNullOrWhiteSpace(options.BotToken) && options.ChatIds.Length > 0;

    private sealed record TelegramNotificationJob(IncidentNotificationEvent? IncidentNotification, bool IsTest);
}
