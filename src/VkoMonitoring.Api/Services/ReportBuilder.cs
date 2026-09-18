using System.Globalization;
using VkoMonitoring.Api.Models;

namespace VkoMonitoring.Api.Services;

public sealed record ReportColumn(string Key, string Label);
public sealed record ReportTable(string[] Headers, IReadOnlyList<object?[]> Rows);

public static class ReportBuilder
{
    public const int MaximumMeasurements = 20_000;
    public static readonly ReportColumn[] MeasurementColumns = [
        new("school", "Школа"), new("computer", "Компьютер"), new("deviceId", "Device ID"), new("room", "Кабинет"),
        new("date", "Дата (UTC)"), new("time", "Время (UTC)"), new("download", "Download, Мбит/с"), new("upload", "Upload, Мбит/с"),
        new("ping", "Ping, мс"), new("jitter", "Jitter, мс"), new("loss", "Packet Loss, %"), new("status", "Статус соединения")];
    public static readonly ReportColumn[] SummaryColumns = [
        new("school", "Школа"), new("count", "Количество замеров"), new("averageDownload", "Средний Download, Мбит/с"),
        new("minimumDownload", "Минимальный Download, Мбит/с"), new("averageUpload", "Средний Upload, Мбит/с"),
        new("averagePing", "Средний Ping, мс"), new("problems", "Проблемных замеров"), new("problemPercent", "Доля проблемных замеров, %"),
        new("offline", "Замеров без соединения")];

    public static ReportColumn[] SelectColumns(bool summary, string[]? fields)
    {
        var allowed = summary ? SummaryColumns : MeasurementColumns;
        if (fields is null) return allowed;
        if (fields.Length == 0 || fields.Any(field => !allowed.Any(column => column.Key == field)))
            throw new ArgumentException("Выберите хотя бы один допустимый столбец.");
        return allowed.Where(column => fields.Contains(column.Key)).ToArray();
    }

    public static ReportTable Build(IReadOnlyList<MeasurementReportRow> rows, bool summary, ReportColumn[] columns)
    {
        var values = new List<object?[]>();
        if (summary)
        {
            foreach (var school in rows.GroupBy(row => row.SchoolId))
            {
                var data = school.ToArray(); var problems = data.Count(row => row.IsProblem);
                object? Value(string key) => key switch {
                    "school" => data[0].SchoolName, "count" => data.Length,
                    "averageDownload" => Average(data.Select(row => row.DownloadMbps)),
                    "minimumDownload" => Minimum(data.Select(row => row.DownloadMbps)),
                    "averageUpload" => Average(data.Select(row => row.UploadMbps)),
                    "averagePing" => Average(data.Select(row => row.PingMilliseconds)),
                    "problems" => problems, "problemPercent" => Math.Round(problems * 100d / data.Length, 2),
                    "offline" => data.Count(row => row.ConnectionStatus == "Offline"), _ => null };
                values.Add(columns.Select(column => Value(column.Key)).ToArray());
            }
        }
        else foreach (var row in rows)
        {
            object? Value(string key) => key switch {
                "school" => row.SchoolName, "computer" => row.DeviceName, "deviceId" => row.DeviceId.ToString(), "room" => row.Room,
                "date" => row.MeasuredAtUtc.UtcDateTime.Date,
                "time" => row.MeasuredAtUtc.UtcDateTime.TimeOfDay,
                "download" => row.DownloadMbps, "upload" => row.UploadMbps, "ping" => row.PingMilliseconds,
                "jitter" => row.JitterMilliseconds, "loss" => row.PacketLossPercent, "status" => row.ConnectionStatus, _ => null };
            values.Add(columns.Select(column => Value(column.Key)).ToArray());
        }
        return new ReportTable(columns.Select(column => column.Label).ToArray(), values);
    }

    private static double? Average(IEnumerable<double?> values) => values.Average();
    private static double? Minimum(IEnumerable<double?> values) => values.Min();
}
