namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class ImportStagingRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImportId { get; set; }
    public Guid WorkspaceId { get; set; }
    public int RowNumber { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public bool IsValid { get; set; } = true;
    public bool IsRejected { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DataImport Import { get; set; } = null!;
    public ICollection<ImportStagingValue> Values { get; set; } = new List<ImportStagingValue>();
}
