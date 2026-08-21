namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record ParsedImportFile(
    IReadOnlyList<string> Headers,
    IReadOnlyList<ParsedImportRow> Rows);
