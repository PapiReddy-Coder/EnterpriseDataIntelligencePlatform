using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public static class ImportRules
{
    public const long MaxFileSizeBytes = 25L * 1024 * 1024;

    public static string? ValidateFile(string fileName, long length)
    {
        if (length <= 0) return "The uploaded file is empty.";
        if (length > MaxFileSizeBytes) return "Maximum file size is 25 MB.";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".csv" or ".xlsx" ? null : "Only CSV and XLSX files are supported.";
    }

    public static bool CanStart(string status) => status == ImportStatuses.Created;
    public static bool CanCancel(string status) => status is ImportStatuses.Queued or ImportStatuses.Processing;
    public static string CompletionStatus(int errorCount, int rejectedCount) => errorCount > 0 || rejectedCount > 0 ? ImportStatuses.CompletedWithErrors : ImportStatuses.Completed;

    public static string NormalizeColumnName(string value) =>
        value.Trim().TrimStart('\uFEFF');

    public static string? ValidateKeyColumns(
        IReadOnlyCollection<string> keyColumns,
        IReadOnlyCollection<string> headers)
    {
        if (keyColumns.Count == 0)
            return null;

        var normalizedHeaders = headers
            .Select(NormalizeColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknownKeys = keyColumns
            .Select(NormalizeColumnName)
            .Where(key => !normalizedHeaders.Contains(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return unknownKeys.Length == 0
            ? null
            : $"Unknown key column(s): {string.Join(", ", unknownKeys)}. " +
              $"Available columns: {string.Join(", ", normalizedHeaders.OrderBy(x => x))}.";
    }
}
