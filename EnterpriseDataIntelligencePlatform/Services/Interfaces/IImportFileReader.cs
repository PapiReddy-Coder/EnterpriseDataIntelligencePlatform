using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IImportFileReader
{
    Task<IReadOnlyList<string>> GetWorksheetNamesAsync(string filePath, string extension, CancellationToken ct);
    Task<ParsedImportFile> ReadAsync(string filePath, string extension, string delimiter, bool firstRowContainsHeaders, string? worksheetName, CancellationToken ct);
}
