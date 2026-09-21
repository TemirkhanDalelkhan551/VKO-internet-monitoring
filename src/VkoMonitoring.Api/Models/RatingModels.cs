namespace VkoMonitoring.Api.Models;

public sealed record RatingMetric(string Name, double? Value, string Unit, double Weight, double Penalty);

public sealed record RatingItem(
    int? Rank,
    Guid SchoolId,
    string SchoolName,
    Guid? LineId,
    string? LineName,
    double? Score,
    string Category,
    int MeasurementCount,
    int ProblemMeasurementCount,
    double? ProblemMeasurementPercent,
    int IncidentCount,
    MeasurementFreshness Freshness,
    IReadOnlyList<RatingMetric> Metrics);

public sealed record RatingFormulaItem(string Name, string Rule, double Weight);

public sealed record RatingOverview(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<RatingItem> Schools,
    IReadOnlyList<RatingItem> Lines,
    IReadOnlyList<RatingFormulaItem> Formula);
