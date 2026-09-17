namespace VkoMonitoring.Api.Health;

public sealed record ApiHealthComponent(
    string Name,
    string Status,
    long DurationMilliseconds);

public sealed record ApiHealthReport(
    string Status,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<ApiHealthComponent> Components);
