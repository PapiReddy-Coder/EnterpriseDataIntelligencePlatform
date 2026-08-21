namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class ImportError
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    public Guid WorkspaceId { get; set; }
    public int? RowNumber { get; set; }
    public string? ColumnName { get; set; }
    public string? InvalidValue { get; set; }
    public string ErrorType { get; set; } = ImportErrorTypes.Processing;
    public string? ValidationRule { get; set; }
    public string ErrorDescription { get; set; } = string.Empty;
    public DateTime ErrorTimestampUtc { get; set; } = DateTime.UtcNow;
    public DataImport Import { get; set; } = null!;
}
