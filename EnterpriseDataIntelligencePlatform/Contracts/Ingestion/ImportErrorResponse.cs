namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record ImportErrorResponse(
    Guid Id,
    int? RowNumber,
    string? ColumnName,
    string? InvalidValue,
    string ErrorType,
    string? ValidationRule,
    string ErrorDescription,
    DateTime ErrorTimestampUtc);
