namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SourceImportId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dataset Dataset { get; set; } = null!;
    public ICollection<DatasetRecordValue> Values { get; set; } = new List<DatasetRecordValue>();
}
