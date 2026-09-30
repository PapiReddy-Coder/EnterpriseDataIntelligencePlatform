namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataQualityProfileRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ImportId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public string TriggerType { get; set; } = QualityProfileTriggers.Manual;
    public string Status { get; set; } = QualityProfileStatuses.Queued;
    public int RetryCount { get; set; }
    public bool CancellationRequested { get; set; }
    public int TotalRecords { get; set; }
    public decimal? CompletenessScore { get; set; }
    public decimal? ValidityScore { get; set; }
    public decimal? UniquenessScore { get; set; }
    public decimal? ConsistencyScore { get; set; }
    public decimal? ErrorRate { get; set; }
    public decimal? OverallQualityScore { get; set; }
    public decimal AppliedCompletenessThreshold { get; set; }
    public decimal AppliedValidityThreshold { get; set; }
    public decimal AppliedUniquenessThreshold { get; set; }
    public decimal AppliedConsistencyThreshold { get; set; }
    public decimal AppliedOverallThreshold { get; set; }
    public string ThresholdStatus { get; set; } = QualityThresholdStatuses.NotApplicable;
    public string ScoringVersion { get; set; } = QualityDefaults.ScoringVersion;
    public string? FailureMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public DataImport? Import { get; set; }
    public ICollection<DataQualityColumnMetric> ColumnMetrics { get; set; } = new List<DataQualityColumnMetric>();
    public ICollection<DataQualityIssue> Issues { get; set; } = new List<DataQualityIssue>();
}
