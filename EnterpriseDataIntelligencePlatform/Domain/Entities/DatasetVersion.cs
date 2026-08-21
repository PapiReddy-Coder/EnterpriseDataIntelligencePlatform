namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetVersion : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public int VersionNumber { get; set; }
    public bool IsCurrent { get; set; }

    public required string Code { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public required string CategoryName { get; set; }
    public Guid OwnerId { get; set; }
    public required string OwnerName { get; set; }
    public required string DataSourceName { get; set; }
    public required string DataSourceType { get; set; }
    public string DataSourceDescription { get; set; } = string.Empty;
    public required string Status { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string? VersionNotes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
