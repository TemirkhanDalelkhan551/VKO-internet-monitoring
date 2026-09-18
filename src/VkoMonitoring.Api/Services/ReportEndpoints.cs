using System.Globalization;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Persistence;
using VkoMonitoring.Api.Configuration;

namespace VkoMonitoring.Api.Services;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/reports/options", () => Results.Ok(new {
            measurementColumns = ReportBuilder.MeasurementColumns, summaryColumns = ReportBuilder.SummaryColumns,
            maximumMeasurements = ReportBuilder.MaximumMeasurements
        })).RequireRateLimiting("admin-read");

        app.MapGet("/api/reports/export", async (HttpContext context, IMonitoringReadRepository repository, MonitoringApiOptions apiOptions,
            TimeProvider timeProvider, CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;
            var format = query["format"].ToString(); var kind = query["kind"].ToString();
            if (format is not ("csv" or "xlsx") || kind is not ("measurements" or "summary"))
                return Invalid("Укажите формат csv/xlsx и вид measurements/summary.");
            var to = timeProvider.GetUtcNow();
            if (query.ContainsKey("to") && !DateTimeOffset.TryParse(query["to"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out to))
                return Invalid("Укажите корректное окончание периода.");
            var from = to < DateTimeOffset.MinValue.AddDays(7) ? DateTimeOffset.MinValue : to.AddDays(-7);
            if (query.ContainsKey("from") && !DateTimeOffset.TryParse(query["from"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out from)
                || from >= to) return Invalid("Укажите корректное начало и окончание периода.");
            Guid? schoolId = null;
            if (query.ContainsKey("schoolId")) {
                if (!Guid.TryParse(query["schoolId"], out var id)) return Invalid("Некорректный идентификатор школы.");
                schoolId = id;
                if (await repository.GetSchoolAsync(id, cancellationToken) is null) return Results.NotFound();
            }
            var deviceIds = new List<Guid>();
            if (query.ContainsKey("deviceIds")) {
                var values = query["deviceIds"].ToString().Split(',');
                if (schoolId is null || values.Length > 200) return Invalid("Для выбора компьютеров укажите школу; не более 200 компьютеров за один запрос.");
                foreach (var value in values) {
                    if (!Guid.TryParse(value, out var id)) return Invalid("Некорректный идентификатор компьютера."); deviceIds.Add(id);
                }
                var allowedDevices = (await repository.GetSchoolDevicesAsync(schoolId.Value, cancellationToken)).Select(device => device.DeviceId).ToHashSet();
                if (deviceIds.Any(id => !allowedDevices.Contains(id))) return Results.NotFound();
            }
            var status = query.ContainsKey("status") ? query["status"].ToString() : null;
            if (status is not (null or "Online" or "Degraded" or "Offline")) return Invalid("Неизвестный статус соединения.");
            ReportColumn[] columns;
            try { columns = ReportBuilder.SelectColumns(kind == "summary", query.ContainsKey("fields") ? query["fields"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries) : null); }
            catch (ArgumentException error) { return Invalid(error.Message); }
            var filter = new ReportFilter(schoolId, deviceIds.Distinct().ToArray(), from, to, status);
            var rows = await repository.GetReportRowsAsync(filter, ReportBuilder.MaximumMeasurements + 1, cancellationToken);
            if (rows.Count > ReportBuilder.MaximumMeasurements)
                return Results.Problem(title: "Отчёт слишком большой", detail: $"Найдено более {ReportBuilder.MaximumMeasurements} замеров. Уменьшите период или выберите школу/компьютеры. Данные не обрезаны.", statusCode: 422);
            var table = ReportBuilder.Build(rows, kind == "summary", columns);
            context.Response.Headers["X-Report-Measurement-Count"] = rows.Count.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-Report-Row-Count"] = table.Rows.Count.ToString(CultureInfo.InvariantCulture);
            var conditions = new ReportTable(["Условие", "Значение"], [
                ["Вид отчёта", kind == "summary" ? "Сводка по школам" : "Подробные измерения"],
                ["Начало (UTC, включительно)", from.UtcDateTime.ToString("O")], ["Окончание (UTC, исключительно)", to.UtcDateTime.ToString("O")],
                ["Школа", schoolId?.ToString() ?? "Все доступные школы"],
                ["Компьютеры", deviceIds.Count == 0 ? "Все доступные компьютеры" : string.Join(", ", deviceIds.Distinct())],
                ["Статус соединения", status ?? "Все статусы"], ["Количество исходных замеров", rows.Count],
                ["Часовой пояс дат и времени", "UTC"], ["Проблемный замер", apiOptions.StorageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
                    ? "Потеря соединения, сбой измерения или отклонение от порогов, действовавших на момент замера."
                    : "Потеря соединения, сбой измерения или отклонение от текущих порогов качества."]]);
            var bytes = format == "csv" ? ReportFileWriter.Csv(table, cancellationToken) : ReportFileWriter.Xlsx(table, conditions, cancellationToken);
            return Results.File(bytes, format == "csv" ? "text/csv; charset=utf-8" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"vko-{kind}-{from:yyyyMMdd}-{to:yyyyMMdd}.{format}");
        }).RequireRateLimiting("admin-read");
    }
    private static IResult Invalid(string text) => Results.ValidationProblem(new Dictionary<string, string[]> { ["report"] = [text] });
}
