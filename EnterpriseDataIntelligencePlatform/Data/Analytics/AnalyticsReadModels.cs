namespace EnterpriseDataIntelligencePlatform.Data.Analytics;

// Read-only SQL view projections. There are no new copies of dataset/import/profile business data.
public sealed class DatasetAnalyticsRow
{
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long CurrentRecordCount { get; set; }
    public Guid? LatestProfileRunId { get; set; }
    public DateTime? ProfileCompletedAtUtc { get; set; }
    public string? ScoringVersion { get; set; }
    public decimal? OverallQualityScore { get; set; }
    public decimal? Completeness { get; set; }
    public decimal? Validity { get; set; }
    public decimal? Uniqueness { get; set; }
    public decimal? Consistency { get; set; }
    public string? ThresholdStatus { get; set; }
    public decimal? AppliedOverallThreshold { get; set; }
    public decimal? AppliedCompletenessThreshold { get; set; }
    public decimal? AppliedValidityThreshold { get; set; }
    public decimal? AppliedUniquenessThreshold { get; set; }
    public decimal? AppliedConsistencyThreshold { get; set; }
    public Guid? LatestImportId { get; set; }
    public string? LatestImportStatus { get; set; }
    public DateTime? LatestCommittedImportCompletedAtUtc { get; set; }
    public long QualityIssueCount { get; set; }
    public long ErrorIssueCount { get; set; }
    public long InvalidValueCount { get; set; }
    public bool NoCompletedProfile { get; set; }
    public bool NoProfileAfterLatestImport { get; set; }
    public bool FailedQualityThreshold { get; set; }
    public bool LatestImportNeedsAttention { get; set; }
    public bool SignificantQualityIssues { get; set; }
    public bool RequiresAttention { get; set; }
}

public sealed class ImportAnalyticsRow
{
    public Guid ImportId { get; set; }
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string DatasetName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ImportMode { get; set; } = "";
    public Guid InitiatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long RecordsAttempted { get; set; }
    public long RecordsSuccessfullyImported { get; set; }
    public long RecordsRejected { get; set; }
    public long RecordsProcessed { get; set; }
    public long RecordsWithoutFinalOutcome { get; set; }
    public bool StatisticsComplete { get; set; }
    public long? ProcessingTimeMilliseconds { get; set; }
}

public sealed class QualityTrendRow
{
    public Guid ProfileRunId { get; set; }
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public DateTime ProfileCreatedAtUtc { get; set; }
    public DateTime DayUtc { get; set; }
    public DateTime WeekUtc { get; set; }
    public DateTime MonthUtc { get; set; }
    public decimal? OverallQualityScore { get; set; }
    public decimal? Completeness { get; set; }
    public decimal? Validity { get; set; }
    public decimal? Uniqueness { get; set; }
    public decimal? Consistency { get; set; }
}
