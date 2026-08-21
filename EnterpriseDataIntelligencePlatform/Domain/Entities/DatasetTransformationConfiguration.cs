namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetTransformationConfiguration : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dataset Dataset { get; set; } = null!;
}
