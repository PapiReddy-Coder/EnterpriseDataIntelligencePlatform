namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetAccessGrant : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string AccessLevel { get; set; } = DatasetAccessLevels.Read;
    public Guid GrantedByUserId { get; set; }
    public AppUser GrantedByUser { get; set; } = null!;
    public DateTime GrantedDateUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiryDateUtc { get; set; }
    public string Status { get; set; } = AccessGrantStatuses.Active;
    public Guid? RevokedByUserId { get; set; }
    public AppUser? RevokedByUser { get; set; }
    public DateTime? RevokedDateUtc { get; set; }
    public Guid SourceAccessRequestId { get; set; }
    public DatasetAccessRequest SourceAccessRequest { get; set; } = null!;
}
