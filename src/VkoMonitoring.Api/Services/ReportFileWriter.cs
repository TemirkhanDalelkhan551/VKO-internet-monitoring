using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace VkoMonitoring.Api.Services;

public static class ReportFileWriter
{
    private const string SheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Csv(ReportTable table, CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        using (var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true))
        {
            writer.Write(string.Join(';', table.Headers.Select(CsvText))); writer.Write("\r\n");
            foreach (var row in table.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(string.Join(';', row.Select(value => value switch {
                    string text => CsvText(text), DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TimeSpan time => time.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture), _ => Number(value) })));
                writer.Write("\r\n");
            }
        }
        return output.ToArray();
    }

    private static string CsvText(string value)
    {
        // Spreadsheet programs must treat untrusted school/device names as text, never formulas.
        if (value.Length > 0 && ("=+-@".Contains(value.TrimStart().FirstOrDefault()) || "\t\r\n".Contains(value[0]))) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string Number(object? value) => value is null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    public static byte[] Xlsx(ReportTable table, ReportTable conditions, CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
                """);
            WriteText(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            WriteText(zip, "xl/workbook.xml", """
                <?xml version="1.0" encoding="utf-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Отчёт" sheetId="1" r:id="rId1"/><sheet name="Условия" sheetId="2" r:id="rId2"/></sheets></workbook>
                """);
            WriteText(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                """);
            WriteText(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="utf-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/><numFmt numFmtId="165" formatCode="hh:mm:ss.000"/><numFmt numFmtId="166" formatCode="0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Arial"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Arial"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF153E37"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="center"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1" applyAlignment="1"><alignment wrapText="1" vertical="center"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                """);
            WriteSheet(zip, "xl/worksheets/sheet1.xml", table, cancellationToken);
            WriteSheet(zip, "xl/worksheets/sheet2.xml", conditions, cancellationToken);
        }
        return output.ToArray();
    }
    private static void WriteText(ZipArchive zip, string path, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text);
    }
    private static void WriteSheet(ZipArchive zip, string path, ReportTable table, CancellationToken cancellationToken)
    {
        using var stream = zip.CreateEntry(path).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument(); writer.WriteStartElement("worksheet", SheetNamespace);
        writer.WriteStartElement("sheetViews"); writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "1"); writer.WriteAttributeString("topLeftCell", "A2"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("cols");
        var widths = table.Headers.Select((header, index) => table.Headers.Length == 2 && index == 1 ? 90d : Math.Clamp(
            Math.Max(header.Length, table.Rows.Take(2000).Select(row => row[index] is string text ? text.Length : 12).DefaultIfEmpty(12).Max()) + 2, index == 0 ? 40 : 16, 48)).ToArray();
        for (var index = 0; index < table.Headers.Length; index++) {
            writer.WriteStartElement("col"); writer.WriteAttributeString("min", (index + 1).ToString()); writer.WriteAttributeString("max", (index + 1).ToString());
            writer.WriteAttributeString("width", widths[index].ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("customWidth", "1"); writer.WriteEndElement();
        }
        writer.WriteEndElement(); writer.WriteStartElement("sheetData");
        WriteRow(writer, table.Headers.Cast<object?>().ToArray(), 1, true, widths);
        for (var index = 0; index < table.Rows.Count; index++) { cancellationToken.ThrowIfCancellationRequested(); WriteRow(writer, table.Rows[index], index + 2, false, widths); }
        writer.WriteEndElement(); writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A1:{ColumnName(table.Headers.Length - 1)}{table.Rows.Count + 1}"); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndDocument();
    }
    private static void WriteRow(XmlWriter writer, object?[] row, int number, bool header, double[] widths)
    {
        writer.WriteStartElement("row"); writer.WriteAttributeString("r", number.ToString());
        var height = header ? 34 : Math.Max(20, row.Select((value, index) => value is string text ? text.Split('\n').Sum(line => Math.Max(1, Math.Ceiling(line.Length / (widths[index] - 2)))) * 15 + 5 : 20).DefaultIfEmpty(20).Max());
        writer.WriteAttributeString("ht", height.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("customHeight", "1");
        for (var index = 0; index < row.Length; index++) {
            var value = row[index]; if (value is null) continue;
            writer.WriteStartElement("c"); writer.WriteAttributeString("r", ColumnName(index) + number);
            if (header) writer.WriteAttributeString("s", "1");
            else if (value is DateTime) writer.WriteAttributeString("s", "2");
            else if (value is TimeSpan) writer.WriteAttributeString("s", "3");
            else if (value is double or decimal) writer.WriteAttributeString("s", "4");
            if (value is string text) {
                writer.WriteAttributeString("t", "inlineStr"); writer.WriteStartElement("is"); writer.WriteStartElement("t"); writer.WriteAttributeString("xml", "space", null, "preserve");
                writer.WriteString(string.Concat(text.EnumerateRunes().Where(rune => rune.Value is 9 or 10 or 13 || rune.Value >= 32 && rune.Value is not 0xFFFE and not 0xFFFF).Select(rune => rune.ToString())));
                writer.WriteEndElement(); writer.WriteEndElement();
            } else writer.WriteElementString("v", Number(value switch { DateTime date => date.ToOADate(), TimeSpan time => time.TotalDays, _ => value }));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }
    private static string ColumnName(int index) {
        var name = ""; do { name = (char)('A' + index % 26) + name; index = index / 26 - 1; } while (index >= 0); return name;
    }
}
