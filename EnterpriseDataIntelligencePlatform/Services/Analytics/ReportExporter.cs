using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Analytics;

public sealed record ReportExportFile(byte[] Bytes, string ContentType, string FileName);

public interface IReportExporter
{
    ReportExportFile Export(ReportData report, string format, CancellationToken ct);
}

public sealed class ReportExporter : IReportExporter
{
    public ReportExportFile Export(ReportData report, string format, CancellationToken ct)
    {
        var name = $"{report.ReportType}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return format switch
        {
            "csv" => new(Csv(report, ct), "text/csv; charset=utf-8", name + ".csv"),
            "xlsx" => new(Excel(report, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx"),
            _ => throw new AnalyticsRequestException(400, "InvalidExportFormat", "Format must be csv or xlsx.")
        };
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "",
        DateTime date => DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    public static string CsvCell(object? value)
    {
        var text = FormatValue(value);
        if (value is string)
        {
            var trimmed = new string(text.SkipWhile(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\uFEFF').ToArray());
            if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) ||
                (text.Length > 0 && char.IsControl(text[0])))
                text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] Csv(ReportData report, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            writer.WriteLine(string.Join(",", report.Columns.Select(x => CsvCell(x.Header))));
            foreach (var row in report.Rows)
            {
                ct.ThrowIfCancellationRequested();
                writer.WriteLine(string.Join(",", report.Columns.Select(x => CsvCell(row.GetValueOrDefault(x.Key)))));
            }
        }
        return stream.ToArray();
    }

    private static byte[] Excel(ReportData report, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new Fonts(new Font(new FontSize { Val = 11 }), new Font(new Bold(), new FontSize { Val = 11 })) { Count = 2 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8EEF6" }) { PatternType = PatternValues.Solid })) { Count = 3 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(new CellFormat(),
                    new CellFormat { FontId = 1, FillId = 2, BorderId = 0, ApplyFont = true, ApplyFill = true }) { Count = 2 });
            styles.Stylesheet.Save();
            var sheets = workbook.Workbook.AppendChild(new Sheets());
            var summaryRows = report.Summary.Select(x => (IReadOnlyList<object?>)new object?[] { x.Key, x.Value });
            WriteSheet(workbook, sheets, 1, "Summary", ["Metric", "Value"], summaryRows, ct);
            var detailRows = report.Rows.Select(row =>
                (IReadOnlyList<object?>)report.Columns.Select(column => row.GetValueOrDefault(column.Key)).ToArray());
            WriteSheet(workbook, sheets, 2, "Details", report.Columns.Select(x => x.Header).ToArray(), detailRows, ct);
            workbook.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void WriteSheet(WorkbookPart workbook, Sheets sheets, uint id, string name,
        IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, CancellationToken ct)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(part))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteElement(new SheetViews(new SheetView(
                new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
                { WorkbookViewId = 0 }));
            writer.WriteElement(new Columns(new Column { Min = 1, Max = (uint)headers.Count, Width = 25, CustomWidth = true }));
            writer.WriteStartElement(new SheetData());
            uint index = 1;
            WriteRow(writer, index++, headers.Cast<object?>().ToArray(), true);
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                WriteRow(writer, index++, row, false);
            }
            writer.WriteEndElement();
            writer.WriteElement(new AutoFilter { Reference = $"A1:{ColumnName(headers.Count)}{index - 1}" });
            writer.WriteEndElement();
        }
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
    }

    private static void WriteRow(OpenXmlWriter writer, uint index, IReadOnlyList<object?> values, bool header)
    {
        writer.WriteStartElement(new Row { RowIndex = index });
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            var cell = new Cell { CellReference = ColumnName(i + 1) + index, StyleIndex = header ? 1U : 0U };
            if (value is byte or short or int or long or float or double or decimal)
            {
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(FormatValue(value));
            }
            else if (value is bool flag)
            {
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(flag ? "1" : "0");
            }
            else
            {
                // Inline strings are never formulas, even when beginning with '='.
                cell.DataType = CellValues.InlineString;
                var text = CleanXmlText(FormatValue(value));
                cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }

    private static string CleanXmlText(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
            if (rune.Value is 9 or 10 or 13 || (rune.Value >= 32 && rune.Value != 0xFFFE && rune.Value != 0xFFFF))
                result.Append(rune.ToString());
        return result.ToString();
    }

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; }
        return name;
    }
}
