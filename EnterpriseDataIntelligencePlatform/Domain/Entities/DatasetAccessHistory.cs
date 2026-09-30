namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetAccessHistory : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Guid AccessRequestId { get; set; }
    public DatasetAccessRequest AccessRequest { get; set; } = null!;
    public string Action { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public Guid PerformedByUserId { get; set; }
    public AppUser PerformedByUser { get; set; } = null!;
    public string? Comments { get; set; }
    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
}
