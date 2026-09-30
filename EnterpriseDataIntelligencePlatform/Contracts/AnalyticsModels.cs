using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

/// <summary>Shared dataset scope. Dates are UTC, inclusive FromUtc and exclusive ToUtc.
/// Status is a DATASET status; ImportStatus and QualityStatus are separate filters.</summary>
public class AnalyticsQuery
{
    public Guid? WorkspaceId { get; set; }
    public Guid? DatasetId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? OwnerId { get; set; }
    [StringLength(200)] public string? Search { get; set; }
    public string? Status { get; set; }
    public string? ImportStatus { get; set; }
    public string? QualityStatus { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    /// <summary>created (default), updated for datasets, started/completed for imports.</summary>
    public string? DateField { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "desc";
}

public sealed class TrendQuery : AnalyticsQuery
{
    public TrendQuery() { SortDirection = "asc"; PageSize = 100; }
    /// <summary>daily, weekly (Monday UTC), or monthly.</summary>
    public string Grouping { get; set; } = "daily";
}

public sealed class ReportExportQuery : AnalyticsQuery
{
    public string Format { get; set; } = "csv";
    /// <summary>all (full filtered results, bounded) or page (the requested page).</summary>
    public string Scope { get; set; } = "all";
}

public sealed record AnalyticsError(string Code, string Message, IReadOnlyList<string>? Details = null);
public sealed record AnalyticsMetadata(DateTime GeneratedAtUtc, string Timezone, string TraceId,
    IReadOnlyDictionary<string, string> Filters);
public sealed record AnalyticsPagination(int Page, int PageSize, long TotalCount, long TotalPages);
public sealed record AnalyticsResponse<T>(bool Success, T? Data, AnalyticsMetadata Metadata,
    AnalyticsPagination? Pagination, AnalyticsError? Error);

public sealed record AnalyticsPage<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    public AnalyticsPagination Pagination => new(Page, PageSize, TotalCount,
        (long)Math.Ceiling(TotalCount / (decimal)PageSize));
}

public sealed class QualitySummary
{
    public long TotalDatasets { get; set; }
    public long ProfiledDatasets { get; set; }
    public long ScoredDatasets { get; set; }
    public long UnprofiledDatasets { get; set; }
    public long NotApplicableDatasets { get; set; }
    public long PassingDatasets { get; set; }
    public long FailingDatasets { get; set; }
    public decimal? OverallQualityScore { get; set; }
    public decimal? Completeness { get; set; }
    public decimal? Validity { get; set; }
    public decimal? Uniqueness { get; set; }
    public decimal? Consistency { get; set; }
    public long QualityIssueCount { get; set; }
}

public class ImportSummary
{
    public long TotalImports { get; set; }
    public long SuccessfulImports { get; set; }
    public long FailedImports { get; set; }
    public long CompletedWithErrors { get; set; }
    public long CancelledImports { get; set; }
    public long PendingImports { get; set; }
    public long RecordsAttempted { get; set; }
    public long RecordsSuccessfullyImported { get; set; }
    public long RecordsRejected { get; set; }
    public long TotalRecordsProcessed { get; set; }
    public long RecordsWithoutFinalOutcome { get; set; }
    public long ImportsWithIncompleteStatistics { get; set; }
    public decimal? ImportSuccessRate { get; set; }
    public decimal? RecordSuccessRate { get; set; }
    public double? AverageProcessingTimeMilliseconds { get; set; }
}

public sealed record DashboardSummary(long TotalDatasets, long ActiveDatasets, long ArchivedDatasets,
    long DraftDatasets, long DatasetsRequiringAttention, decimal? AverageDatasetQualityScore,
    ImportSummary Imports, QualitySummary Quality);
public sealed class DatasetDistribution
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long DatasetCount { get; set; }
}
public sealed class QualityIssueAggregate
{
    public string IssueType { get; set; } = "";
    public string Severity { get; set; } = "";
    public long IssueCount { get; set; }
    public long AffectedDatasets { get; set; }
}
public sealed record AttentionDataset(Guid DatasetId, string DatasetName, Guid WorkspaceId,
    decimal? QualityScore, string? LatestImportStatus, IReadOnlyList<string> Reasons);
public sealed record DatasetImportActivity(Guid DatasetId, string DatasetName, Guid WorkspaceId, ImportSummary Imports);

public sealed class QualityTrendPoint
{
    public DateTime BucketUtc { get; set; }
    public long DatasetCount { get; set; }
    public long ScoredDatasetCount { get; set; }
    public decimal? OverallQualityScore { get; set; }
    public decimal? Completeness { get; set; }
    public decimal? Validity { get; set; }
    public decimal? Uniqueness { get; set; }
    public decimal? Consistency { get; set; }
}

public sealed record ImportTrendPoint(DateTime BucketUtc, ImportSummary Imports);

public sealed class WorkspaceSummary
{
    public Guid WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";
    public bool IsActive { get; set; }
    public long DatasetCount { get; set; }
    public long ActiveDatasetCount { get; set; }
    public long DatasetsRequiringAttention { get; set; }
    public decimal? QualityScore { get; set; }
    public long QualityIssues { get; set; }
    public ImportSummary Imports { get; set; } = new();
    public AnalyticsPage<DatasetDistribution>? DatasetDistributionByCategory { get; set; }
}

public sealed record ReportColumn(string Key, string Header, string Type);
public sealed record ReportData(string ReportType, IReadOnlyDictionary<string, object?> Summary,
    IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
public sealed record ReportResult(ReportData Data, AnalyticsPagination Pagination);
