namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataQualityThreshold : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public decimal MinimumCompleteness { get; set; } = QualityDefaults.CompletenessThreshold;
    public decimal MinimumValidity { get; set; } = QualityDefaults.ValidityThreshold;
    public decimal MinimumUniqueness { get; set; } = QualityDefaults.UniquenessThreshold;
    public decimal MinimumConsistency { get; set; } = QualityDefaults.ConsistencyThreshold;
    public decimal MinimumOverallScore { get; set; } = QualityDefaults.OverallThreshold;
    public Guid CreatedByUserId { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Dataset Dataset { get; set; } = null!;
}
