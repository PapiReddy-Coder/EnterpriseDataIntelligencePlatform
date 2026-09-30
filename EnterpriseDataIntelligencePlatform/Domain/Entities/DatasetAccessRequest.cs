namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetAccessRequest : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid RequestingUserId { get; set; }
    public AppUser RequestingUser { get; set; } = null!;
    public string RequestedAccessLevel { get; set; } = DatasetAccessLevels.Read;
    public string BusinessJustification { get; set; } = string.Empty;
    public DateTime RequestDateUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = AccessRequestStatuses.Pending;
    public Guid? ReviewerId { get; set; }
    public AppUser? Reviewer { get; set; }
    public string? ReviewComments { get; set; }
    public DateTime? ReviewedDateUtc { get; set; }
    public DateTime? RequestedExpiryDateUtc { get; set; }
}
