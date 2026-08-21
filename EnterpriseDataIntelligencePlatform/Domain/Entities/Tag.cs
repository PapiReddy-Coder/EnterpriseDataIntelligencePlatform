namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class Tag : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<DatasetTag> DatasetTags { get; set; } = new List<DatasetTag>();
}
