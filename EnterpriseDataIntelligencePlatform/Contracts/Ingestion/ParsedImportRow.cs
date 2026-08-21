namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record ParsedImportRow(
    int RowNumber,
    IReadOnlyList<string?> Values);
