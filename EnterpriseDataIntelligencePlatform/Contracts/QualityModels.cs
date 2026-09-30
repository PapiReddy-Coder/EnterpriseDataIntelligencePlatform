using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record UpsertQualityThresholdRequest(
    [param: Range(0, 100)] decimal MinimumCompleteness,
    [param: Range(0, 100)] decimal MinimumValidity,
    [param: Range(0, 100)] decimal MinimumUniqueness,
    [param: Range(0, 100)] decimal MinimumConsistency,
    [param: Range(0, 100)] decimal MinimumOverallScore);

public sealed record QualityThresholdResponse(
    Guid DatasetId,
    decimal MinimumCompleteness,
    decimal MinimumValidity,
    decimal MinimumUniqueness,
    decimal MinimumConsistency,
    decimal MinimumOverallScore,
    bool IsSystemDefault,
    DateTime? UpdatedAtUtc);

public sealed record StartQualityProfileResponse(Guid ProfileRunId, string Status, string TriggerType, DateTime QueuedAtUtc);

public sealed record QualityDimensionResponse(string Name, decimal? Score, decimal Threshold, bool? Passed);

public sealed record ImportQualityStatisticsResponse(
    Guid ImportId,
    int AttemptedRecords,
    int ValidAttemptedRecords,
    int RejectedRecords,
    int ErrorCount,
    decimal? ValidationRate,
    decimal? ErrorRate);

public sealed record QualityProfileResponse(
    Guid ProfileRunId,
    Guid DatasetId,
    Guid? ImportId,
    string TriggerType,
    string Status,
    int RetryCount,
    int TotalRecords,
    decimal? Completeness,
    decimal? Validity,
    decimal? Uniqueness,
    decimal? Consistency,
    decimal? ErrorRate,
    decimal? OverallQualityScore,
    string ThresholdStatus,
    string ScoringVersion,
    IReadOnlyList<QualityDimensionResponse> Dimensions,
    ImportQualityStatisticsResponse? ImportStatistics,
    string? FailureMessage,
    DateTime QueuedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ColumnQualityMetricResponse(
    Guid DatasetColumnId,
    string ColumnName,
    string DataType,
    bool IsRequired,
    bool IsKey,
    int TotalRecords,
    int NonNullRecords,
    int NullCount,
    decimal? NullPercentage,
    int UniqueCount,
    int DistinctCount,
    int DuplicateCount,
    int InvalidCount,
    decimal? Completeness,
    decimal? Validity,
    decimal? Uniqueness,
    decimal? Consistency,
    decimal? QualityScore,
    string? MinimumValue,
    string? MaximumValue,
    decimal? AverageValue);

public sealed record QualityIssueResponse(
    Guid Id,
    Guid ProfileRunId,
    Guid? ImportId,
    Guid? DatasetRecordId,
    string? ColumnName,
    string IssueType,
    string Severity,
    string? ValidationRule,
    string Description,
    string? InvalidValue,
    DateTime CreatedAtUtc);

public sealed record QualityTrendPointResponse(
    Guid ProfileRunId,
    Guid? ImportId,
    string TriggerType,
    decimal? OverallQualityScore,
    decimal? Completeness,
    decimal? Validity,
    decimal? Uniqueness,
    decimal? Consistency,
    decimal? ErrorRate,
    string ThresholdStatus,
    DateTime CompletedAtUtc);

public sealed record QualityMetricDeltaResponse(string Name, decimal? Before, decimal? After, decimal? Change);

public sealed record QualityComparisonResponse(
    Guid DatasetId,
    Guid LeftProfileRunId,
    Guid RightProfileRunId,
    IReadOnlyList<QualityMetricDeltaResponse> Metrics);

public sealed record QualityIssueSummaryResponse(string IssueType, int Count, int AffectedRecords);

public sealed record QualityDashboardResponse(
    QualityProfileResponse LatestProfile,
    IReadOnlyList<ColumnQualityMetricResponse> Columns,
    IReadOnlyList<QualityIssueSummaryResponse> TopIssues,
    IReadOnlyList<QualityTrendPointResponse> Trend,
    IReadOnlyList<QualityDimensionResponse> FailedThresholds);
