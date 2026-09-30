using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.VisualBasic.FileIO;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class ImportFileReader : IImportFileReader
{
    public Task<IReadOnlyList<string>> GetWorksheetNamesAsync(string filePath, string extension, CancellationToken ct)
    {
        if (!extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbook = document.WorkbookPart?.Workbook ?? throw new InvalidDataException("The Excel workbook is invalid.");
        var names = workbook.Sheets?.Elements<Sheet>().Select(x => x.Name?.Value ?? string.Empty).Where(x => x.Length > 0).ToArray()
                    ?? Array.Empty<string>();
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public Task<ParsedImportFile> ReadAsync(string filePath, string extension, string delimiter, bool firstRowContainsHeaders, string? worksheetName, CancellationToken ct)
    {
        return extension.ToLowerInvariant() switch
        {
            ".csv" => Task.FromResult(ReadCsv(filePath, delimiter, firstRowContainsHeaders, ct)),
            ".xlsx" => Task.FromResult(ReadExcel(filePath, firstRowContainsHeaders, worksheetName, ct)),
            _ => throw new InvalidDataException("Unsupported file format. Only CSV and XLSX are supported.")
        };
    }

    private static ParsedImportFile ReadCsv(string filePath, string delimiter, bool firstRowContainsHeaders, CancellationToken ct)
    {
        using var parser = new TextFieldParser(filePath);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(delimiter);
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        var rawRows = new List<string[]>();
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var fields = parser.ReadFields() ?? Array.Empty<string>();
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            rawRows.Add(fields);
        }
        return BuildParsed(rawRows, firstRowContainsHeaders);
    }

    private static ParsedImportFile ReadExcel(string filePath, bool firstRowContainsHeaders, string? worksheetName, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("The Excel workbook is invalid.");
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToArray() ?? Array.Empty<Sheet>();
        if (sheets.Length == 0) throw new InvalidDataException("The Excel workbook contains no worksheets.");

        Sheet sheet;
        if (string.IsNullOrWhiteSpace(worksheetName))
            throw new InvalidDataException("WorksheetName is required for XLSX imports.");
        else
            sheet = sheets.FirstOrDefault(x => string.Equals(x.Name?.Value, worksheetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Worksheet '{worksheetName}' was not found.");

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var rawRows = new List<string[]>();

        foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
        {
            ct.ThrowIfCancellationRequested();
            var cells = row.Elements<Cell>().ToArray();
            if (cells.Length == 0) continue;
            var maxIndex = cells.Max(c => GetColumnIndex(c.CellReference?.Value));
            var values = Enumerable.Repeat(string.Empty, maxIndex + 1).ToArray();
            foreach (var cell in cells)
            {
                var index = GetColumnIndex(cell.CellReference?.Value);
                values[index] = GetCellText(cell, sharedStrings);
            }
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            rawRows.Add(values);
        }
        return BuildParsed(rawRows, firstRowContainsHeaders);
    }

    private static ParsedImportFile BuildParsed(IReadOnlyList<string[]> rawRows, bool firstRowContainsHeaders)
    {
        if (rawRows.Count == 0) throw new InvalidDataException("The file is empty.");

        var width = rawRows.Max(r => r.Length);
        IReadOnlyList<string> headers;
        var start = 0;
        if (firstRowContainsHeaders)
        {
            headers = Pad(rawRows[0], width)
                .Select(ImportRules.NormalizeColumnName)
                .ToArray();
            start = 1;
        }
        else
        {
            headers = Enumerable.Range(1, width).Select(i => $"Column{i}").ToArray();
        }

        var rows = new List<ParsedImportRow>();
        for (var i = start; i < rawRows.Count; i++)
        {
            var values = Pad(rawRows[i], width).Cast<string?>().ToArray();
            rows.Add(new ParsedImportRow(i + 1, values));
        }
        return new ParsedImportFile(headers, rows);
    }

    private static string[] Pad(string[] values, int width)
    {
        if (values.Length == width) return values;
        var result = new string[width];
        Array.Copy(values, result, values.Length);
        for (var i = values.Length; i < width; i++) result[i] = string.Empty;
        return result;
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(value, out var index) && sharedStrings != null)
            return sharedStrings.ElementAt(index).InnerText;
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.Boolean)
            return value == "1" ? "true" : "false";
        return value;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference)) return 0;
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var index = 0;
        foreach (var c in letters) index = index * 26 + (c - 'A' + 1);
        return Math.Max(0, index - 1);
    }
}
