namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DataType { get; set; } = DatasetColumnTypes.String;
    public int Ordinal { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dataset Dataset { get; set; } = null!;
}
