namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class ImportStagingValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StagingRowId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? OriginalValue { get; set; }
    public string? TransformedValue { get; set; }
    public ImportStagingRow StagingRow { get; set; } = null!;
}
