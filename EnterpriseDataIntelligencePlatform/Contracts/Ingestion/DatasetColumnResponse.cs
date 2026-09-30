namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record DatasetColumnResponse(
    Guid Id,
    string Name,
    string DataType,
    int Ordinal,
    bool IsRequired,
    bool IsKey);
