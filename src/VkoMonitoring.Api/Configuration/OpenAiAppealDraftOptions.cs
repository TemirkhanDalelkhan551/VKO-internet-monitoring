namespace VkoMonitoring.Api.Configuration;

public sealed class OpenAiAppealDraftOptions
{
    public const string SectionName = "OpenAiAppealDraft";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public int TimeoutSeconds { get; init; } = 30;
}
