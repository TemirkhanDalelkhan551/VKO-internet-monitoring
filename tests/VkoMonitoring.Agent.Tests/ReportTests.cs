using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using VkoMonitoring.Api.Models;
using VkoMonitoring.Api.Services;

namespace VkoMonitoring.Agent.Tests;

public sealed class ReportTests
{
    private static readonly Guid School = Guid.NewGuid();
    private static MeasurementReportRow Row(double? download, bool problem = false, string status = "Online", Guid? school = null) =>
        new(school ?? School, "Школа ВКО", Guid.NewGuid(), "Компьютер", "101", DateTimeOffset.Parse("2026-09-18T01:02:03+05:00"),
            download, download, download, 0, 0, status, problem);

    [Fact]
    public void DefaultMeasurementReportContainsEveryRequiredField()
    {
        var table = ReportBuilder.Build([Row(0)], false, ReportBuilder.SelectColumns(false, null));
        Assert.Equal(12, table.Headers.Length);
        Assert.Contains("Кабинет", table.Headers); Assert.Contains("Дата (UTC)", table.Headers);
        Assert.Contains("Packet Loss, %", table.Headers); Assert.Contains("Статус соединения", table.Headers);
        Assert.Equal(new DateTime(2026, 9, 17), table.Rows[0][4]); Assert.Equal(new TimeSpan(20, 2, 3), table.Rows[0][5]);
        Assert.Equal(0d, table.Rows[0][6]);
    }
    [Fact]
    public void SummaryCountsProblemsAndOfflineAndIgnoresNullMetrics()
    {
        var table = ReportBuilder.Build([Row(40), Row(20, true), Row(null, true, "Offline")], true, ReportBuilder.SelectColumns(true, null));
        var row = Assert.Single(table.Rows);
        Assert.Equal(3, row[1]); Assert.Equal(30d, row[2]); Assert.Equal(20d, row[3]);
        Assert.Equal(2, row[6]); Assert.Equal(66.67d, row[7]); Assert.Equal(1, row[8]);
    }
    [Fact]
    public void SummaryGroupsByIdInsteadOfMergingSchoolsWithTheSameName()
    {
        var table = ReportBuilder.Build([Row(40), Row(100, school: Guid.NewGuid())], true, ReportBuilder.SelectColumns(true, null));
        Assert.Equal(2, table.Rows.Count);
    }
    [Fact]
    public void EmptyReportContainsHeadersAndNoInventedSummaryRow()
    {
        Assert.Empty(ReportBuilder.Build([], true, ReportBuilder.SelectColumns(true, null)).Rows);
        var table = ReportBuilder.Build([Row(null, true, "Offline")], true, ReportBuilder.SelectColumns(true, null));
        Assert.Null(table.Rows[0][2]); Assert.Null(table.Rows[0][3]);
    }
    [Theory]
    [InlineData("token")]
    [InlineData("externalIpAddress")]
    [InlineData("averageDownload")]
    public void MeasurementColumnsRejectUnknownOrSensitiveFields(string field) => Assert.Throws<ArgumentException>(() => ReportBuilder.SelectColumns(false, [field]));
    [Fact]
    public void SelectionRejectsEmptyListAndDeduplicatesAllowedColumns()
    {
        Assert.Throws<ArgumentException>(() => ReportBuilder.SelectColumns(false, []));
        var columns = ReportBuilder.SelectColumns(false, ["school", "school", "download"]);
        Assert.Equal(2, columns.Length); Assert.Equal("download", columns[1].Key);
    }
    [Theory]
    [InlineData("=HYPERLINK(\"https://example.com\")")]
    [InlineData("  +SUM(1,2)")]
    [InlineData("@SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("\t=1+2")]
    public void CsvProtectsUntrustedTextFromSpreadsheetFormulaExecution(string value)
    {
        var bytes = ReportFileWriter.Csv(new ReportTable(["Название"], [[value]]));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"'" + value.Replace("\"", "\"\""), text);
    }
    [Fact]
    public void CsvEscapesDelimitersQuotesAndNewlinesAndPreservesEmptyAndZero()
    {
        var text = Encoding.UTF8.GetString(ReportFileWriter.Csv(new ReportTable(["Текст", "Пусто", "Число"], [["А;\"Б\"\nВ", null, 0d]])));
        Assert.Contains("\"А;\"\"Б\"\"\nВ\";;0\r\n", text);
    }
    [Fact]
    public void XlsxUsesNumericCellsAndInlineTextWithoutFormulasOrExternalLinks()
    {
        var table = new ReportTable(["Название", "Ноль", "Пусто"], [["=1+2 школа 🙂", 0d, null]]);
        var bytes = ReportFileWriter.Xlsx(table, new ReportTable(["Условие", "Значение"], [["Статус", "Все"]]));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.NotNull(zip.GetEntry("[Content_Types].xml")); Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(); var xml = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = xml.Descendants(ns + "c").ToArray(); var text = cells.Single(cell => (string?)cell.Attribute("r") == "A2");
        Assert.Equal("inlineStr", (string?)text.Attribute("t")); Assert.Equal("=1+2 школа 🙂", text.Descendants(ns + "t").Single().Value);
        Assert.Equal("0", cells.Single(cell => (string?)cell.Attribute("r") == "B2").Element(ns + "v")!.Value);
        Assert.DoesNotContain(cells, cell => (string?)cell.Attribute("r") == "C2"); Assert.Empty(xml.Descendants(ns + "f"));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("externalLink"));
    }
    [Fact]
    public void BothWritersRespectCancellation()
    {
        var table = new ReportTable(["Номер"], [[1]]); var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => ReportFileWriter.Csv(table, token));
        Assert.Throws<OperationCanceledException>(() => ReportFileWriter.Xlsx(table, table, token));
    }
}
