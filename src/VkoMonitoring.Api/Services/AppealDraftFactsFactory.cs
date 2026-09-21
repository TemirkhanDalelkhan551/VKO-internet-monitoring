using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Services;

public static class AppealDraftFactsFactory
{
    public static AppealDraftFacts Create(IncidentOverview incident, IEnumerable<MeasurementReportRow> source)
    {
        var rows = source.ToArray();
        return new AppealDraftFacts(incident.SchoolName, incident.LineName, incident.ProviderName,
            incident.IncidentNumber, incident.ProblemType, incident.Description, incident.StartedAtUtc,
            rows.Length, rows.Count(row => row.IsProblem), Average(rows.Select(row => row.DownloadMbps)),
            Average(rows.Select(row => row.UploadMbps)), Average(rows.Select(row => row.PingMilliseconds)),
            Average(rows.Select(row => row.JitterMilliseconds)), Average(rows.Select(row => row.PacketLossPercent)));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var numbers = values.OfType<double>().ToArray();
        return numbers.Length == 0 ? null : Math.Round(numbers.Average(), 2);
    }
}
