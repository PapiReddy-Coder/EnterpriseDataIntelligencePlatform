namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetGovernance : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid? DataStewardId { get; set; }
    public AppUser? DataSteward { get; set; }
    public string BusinessDescription { get; set; } = string.Empty;
    public string GovernanceStatus { get; set; } = GovernanceStatuses.Draft;
    public string Classification { get; set; } = DataClassifications.Internal;
    public DateTime? LastReviewedDateUtc { get; set; }
    public DateTime? NextReviewDateUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
