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
    private readonly Channel<IncidentNotificationEvent> _queue = Channel.CreateBounded<IncidentNotificationEvent>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

    public bool TryEnqueue(IncidentNotificationEvent notification)
    {
        if (!IsConfigured()) return true;
        if (_queue.Writer.TryWrite(notification)) return true;
        logger.LogWarning("Telegram notification queue is full; incident {IncidentId} was not queued.", notification.IncidentId);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var incident = await incidents.GetIncidentAsync(notification.IncidentId, stoppingToken);
                if (incident is not null) await DeliverAsync(incident.Incident, notification.Kind, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram notification for incident {IncidentId} was not delivered.", notification.IncidentId);
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

    private bool IsConfigured() => options.Enabled && !string.IsNullOrWhiteSpace(options.BotToken) && options.ChatIds.Length > 0;
}
