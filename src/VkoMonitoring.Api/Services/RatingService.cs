using VkoMonitoring.Api.Configuration;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;

namespace VkoMonitoring.Api.Services;

public sealed class RatingService(
    IMonitoringReadRepository monitoring,
    IIncidentRepository incidents,
    IOperationalSettingsRepository settings,
    TimeProvider timeProvider)
{
    public const int MaximumMeasurements = 50_000;

    public async Task<RatingOverview> BuildAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var rows = await monitoring.GetReportRowsAsync(new ReportFilter(null, [], fromUtc, toUtc, null), MaximumMeasurements + 1, cancellationToken);
        if (rows.Count > MaximumMeasurements) throw new InvalidOperationException("Too many measurements for rating.");
        var schools = await monitoring.GetSchoolsAsync(cancellationToken);
        var allIncidents = await incidents.GetIncidentsAsync(null, null, fromUtc, toUtc, 10_000, cancellationToken);
        var thresholds = await settings.GetAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var schoolItems = schools.Select(school => BuildItem(school.SchoolId, school.Name, null, null,
            rows.Where(row => row.SchoolId == school.SchoolId), allIncidents.Count(incident => incident.SchoolId == school.SchoolId),
            school.MeasurementFreshness, thresholds)).ToArray();
        var lineItems = schools.SelectMany(school => school.Lines.Select(line => BuildItem(school.SchoolId, school.Name,
            line.LineId, line.Name, rows.Where(row => row.LineId == line.LineId),
            allIncidents.Count(incident => incident.LineId == line.LineId), line.MeasurementFreshness, thresholds))).ToArray();
        return new RatingOverview(fromUtc, toUtc, Rank(schoolItems), Rank(lineItems), Formula());
    }

    public static RatingItem BuildItem(Guid schoolId, string schoolName, Guid? lineId, string? lineName,
        IEnumerable<MeasurementReportRow> source, int incidentCount, MeasurementFreshness freshness, OperationalSettings thresholds)
    {
        var rows = source.ToArray();
        if (rows.Length == 0) return new RatingItem(0, schoolId, schoolName, lineId, lineName, null, "Нет данных", 0, 0, null, incidentCount, freshness, []);
        var problemPercent = rows.Count(row => row.IsProblem) * 100d / rows.Length;
        var metrics = new List<RatingMetric>
        {
            new("Проблемные замеры", problemPercent, "%", 35, 35 * problemPercent / 100),
            LowerIsBetter("Download", Average(rows.Select(row => row.DownloadMbps)), (double)thresholds.MinimumDownloadMbps, "Мбит/с", 15),
            LowerIsBetter("Upload", Average(rows.Select(row => row.UploadMbps)), (double)thresholds.MinimumUploadMbps, "Мбит/с", 10),
            HigherIsBetter("Ping", Average(rows.Select(row => row.PingMilliseconds)), (double)thresholds.MaximumPingMilliseconds, "мс", 10),
            HigherIsBetter("Jitter", Average(rows.Select(row => row.JitterMilliseconds)), (double)thresholds.MaximumJitterMilliseconds, "мс", 5),
            HigherIsBetter("Packet Loss", Average(rows.Select(row => row.PacketLossPercent)), (double)thresholds.MaximumPacketLossPercent, "%", 5),
            new("Свежесть данных", freshness == MeasurementFreshness.Fresh ? 0 : freshness == MeasurementFreshness.Stale ? 1 : 2, "", 10, freshness == MeasurementFreshness.Fresh ? 0 : freshness == MeasurementFreshness.Stale ? 5 : 10),
            new("Инциденты", incidentCount, "шт.", 10, Math.Min(10, incidentCount * 2d))
        };
        var score = Math.Round(Math.Max(0, 100 - metrics.Sum(metric => metric.Penalty)), 1);
        return new RatingItem(0, schoolId, schoolName, lineId, lineName, score,
            score >= 85 ? "Стабильно" : score >= 60 ? "Требует внимания" : "Проблемно",
            rows.Length, rows.Count(row => row.IsProblem), Math.Round(problemPercent, 1), incidentCount, freshness, metrics);
    }

    private static IReadOnlyList<RatingItem> Rank(IEnumerable<RatingItem> items) => items.OrderByDescending(item => item.Score ?? -1)
        .ThenBy(item => item.SchoolName, StringComparer.Ordinal).ThenBy(item => item.LineName, StringComparer.Ordinal)
        .Select((item, index) => item with { Rank = index + 1 }).ToArray();
    private static double? Average(IEnumerable<double?> values) { var numbers = values.OfType<double>().ToArray(); return numbers.Length == 0 ? null : Math.Round(numbers.Average(), 2); }
    private static RatingMetric LowerIsBetter(string name, double? value, double target, string unit, double weight) =>
        new(name, value, unit, weight, value is null ? weight : weight * Math.Clamp((target - value.Value) / target, 0, 1));
    private static RatingMetric HigherIsBetter(string name, double? value, double target, string unit, double weight) =>
        new(name, value, unit, weight, value is null ? weight : weight * Math.Clamp((value.Value - target) / target, 0, 1));
    private static IReadOnlyList<RatingFormulaItem> Formula() =>
    [
        new("Итог", "100 − сумма штрафов; ниже 0 не опускается.", 100),
        new("Проблемные замеры", "35 × доля проблемных замеров.", 35),
        new("Download / Upload", "Штраф пропорционален дефициту относительно порога.", 25),
        new("Ping / Jitter / Packet Loss", "Штраф пропорционален превышению порога.", 20),
        new("Свежесть", "Свежие: 0; устаревшие: 5; отсутствуют: 10.", 10),
        new("Инциденты", "2 балла за инцидент за период, максимум 10.", 10)
    ];
}
