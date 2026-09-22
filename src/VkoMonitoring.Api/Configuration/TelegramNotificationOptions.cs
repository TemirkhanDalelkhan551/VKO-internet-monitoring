namespace VkoMonitoring.Api.Configuration;

/// <summary>Optional Telegram delivery. Secrets are supplied only through environment variables.</summary>
public sealed class TelegramNotificationOptions
{
    public const string SectionName = "TelegramNotifications";

    public bool Enabled { get; init; }
    public string BotToken { get; init; } = string.Empty;
    public string[] ChatIds { get; init; } = [];
    public string PublicAppUrl { get; init; } = "https://vko-internet-monitoring-api.onrender.com/";
    public int TimeoutSeconds { get; init; } = 8;
}
