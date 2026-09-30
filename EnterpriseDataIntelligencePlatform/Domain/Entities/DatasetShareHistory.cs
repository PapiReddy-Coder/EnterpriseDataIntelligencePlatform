namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetShareHistory : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid DatasetAccessGrantId { get; set; }
    public Guid RecipientUserId { get; set; }
    public AppUser RecipientUser { get; set; } = null!;
    public Guid SharedByUserId { get; set; }
    public AppUser SharedByUser { get; set; } = null!;
    public string AccessLevel { get; set; } = DatasetAccessLevels.Read;
    public DateTime SharedDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiryDateUtc { get; set; }
    public string Status { get; set; } = DatasetShareStatuses.Active;
    public string Action { get; set; } = DatasetShareActions.Shared;
    public DateTime? RevokedDateUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public AppUser? RevokedByUser { get; set; }
    public string? Reason { get; set; }
}
