namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class Dataset : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string Description { get; set; } = string.Empty;

    public Guid CategoryId { get; set; }
    public DatasetCategory Category { get; set; } = null!;

    public Guid OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    public required string DataSourceName { get; set; }
    public required string DataSourceType { get; set; }
    public string DataSourceDescription { get; set; } = string.Empty;

    public string Status { get; set; } = DatasetStatuses.Draft;
    public int CurrentVersion { get; set; } = 1;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public ICollection<DatasetTag> DatasetTags { get; set; } = new List<DatasetTag>();
    public ICollection<DatasetVersion> Versions { get; set; } = new List<DatasetVersion>();
}
