namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetCertification : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid SubmittedByUserId { get; set; }
    public AppUser SubmittedByUser { get; set; } = null!;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CertifiedByUserId { get; set; }
    public AppUser? CertifiedByUser { get; set; }
    public DateTime? CertifiedDateUtc { get; set; }
    public string CertificationStatus { get; set; } = CertificationStatuses.Pending;
    public DateTime? ReviewExpiryDateUtc { get; set; }
    public string? Comments { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}
