using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseDataIntelligencePlatform.Services.Analytics;

public interface IReportService
{
    Task<ReportResult> GenerateAsync(string reportType, AnalyticsQuery filter, bool exportAll, CancellationToken ct);
}

public sealed class ReportService(AnalyticsQueries queries, AnalyticsService analytics,
    IAuditService audit, IOptions<AnalyticsOptions> options) : IReportService
{
    public static readonly string[] ReportTypes =
        ["dataset-summary", "data-quality", "import-activity", "data-processing", "workspace-summary"];

    public async Task<ReportResult> GenerateAsync(string reportType, AnalyticsQuery filter, bool exportAll, CancellationToken ct)
    {
        if (!ReportTypes.Contains(reportType))
            throw new AnalyticsRequestException(400, "InvalidReportType", $"ReportType must be: {string.Join(", ", ReportTypes)}.");
        await queries.ValidateScopeAsync(filter, ct);
        ReportResult result;
        switch (reportType)
        {
            case "dataset-summary":
            {
                var query = queries.Datasets(filter);
                var page = await LoadAsync(AnalyticsQueries.SortDatasets(query, filter), filter, exportAll, ct);
                var quality = AnalyticsRules.Finish(await query.AggregateQuality().SingleOrDefaultAsync(ct) ?? new());
                var summary = QualitySummaryValues(quality);
                summary["CurrentRecordCount"] = await query.SumAsync(x => (long?)x.CurrentRecordCount, ct) ?? 0;
                result = Make(reportType, page, DatasetColumns, summary, x => Values(
                    ("DatasetId", x.DatasetId), ("Code", x.Code), ("DatasetName", x.Name), ("WorkspaceId", x.WorkspaceId),
                    ("Workspace", x.WorkspaceName), ("Category", x.CategoryName), ("Owner", x.OwnerName), ("Status", x.Status),
                    ("CurrentRecordCount", x.CurrentRecordCount), ("QualityScore", x.OverallQualityScore),
                    ("RequiresAttention", x.RequiresAttention), ("AttentionReasons", string.Join("; ", AnalyticsRules.AttentionReasons(x))),
                    ("CreatedAtUtc", x.CreatedAtUtc), ("UpdatedAtUtc", x.UpdatedAtUtc)));
                break;
            }
            case "data-quality":
            {
                var query = queries.Quality(filter);
                var page = await LoadAsync(AnalyticsQueries.SortDatasets(query, filter), filter, exportAll, ct);
                var quality = AnalyticsRules.Finish(await query.AggregateQuality().SingleOrDefaultAsync(ct) ?? new());
                result = Make(reportType, page, QualityColumns, QualitySummaryValues(quality), x => Values(
                    ("DatasetId", x.DatasetId), ("DatasetName", x.Name), ("WorkspaceId", x.WorkspaceId), ("Workspace", x.WorkspaceName),
                    ("ProfileRunId", x.LatestProfileRunId), ("ProfileCompletedAtUtc", x.ProfileCompletedAtUtc),
                    ("QualityScore", x.OverallQualityScore), ("Completeness", x.Completeness), ("Validity", x.Validity),
                    ("Uniqueness", x.Uniqueness), ("Consistency", x.Consistency), ("ThresholdStatus", x.ThresholdStatus ?? "Unprofiled"),
                    ("OverallThreshold", x.AppliedOverallThreshold), ("CompletenessThreshold", x.AppliedCompletenessThreshold),
                    ("ValidityThreshold", x.AppliedValidityThreshold), ("UniquenessThreshold", x.AppliedUniquenessThreshold),
                    ("ConsistencyThreshold", x.AppliedConsistencyThreshold), ("IssueCount", x.QualityIssueCount),
                    ("InvalidValueCount", x.InvalidValueCount), ("AttentionReasons", string.Join("; ", AnalyticsRules.AttentionReasons(x))),
                    ("ScoringVersion", x.ScoringVersion)));
                break;
            }
            case "import-activity":
            {
                var query = queries.Imports(filter);
                var page = await LoadAsync(AnalyticsQueries.SortImports(query, filter), filter, exportAll, ct);
                var summary = AnalyticsRules.Finish(await query.GroupBy(x => 1).AggregateImports().SingleOrDefaultAsync(ct) ?? new());
                result = Make(reportType, page, ImportColumns, ImportSummaryValues(summary), x => Values(
                    ("ImportId", x.ImportId), ("DatasetId", x.DatasetId), ("DatasetName", x.DatasetName),
                    ("WorkspaceId", x.WorkspaceId), ("Status", x.Status), ("ImportMode", x.ImportMode),
                    ("RecordsAttempted", x.RecordsAttempted), ("RecordsSuccessfullyImported", x.RecordsSuccessfullyImported),
                    ("RecordsRejected", x.RecordsRejected), ("TotalRecordsProcessed", x.RecordsProcessed),
                    ("RecordsWithoutFinalOutcome", x.RecordsWithoutFinalOutcome), ("StatisticsComplete", x.StatisticsComplete),
                    ("ProcessingTimeMilliseconds", x.ProcessingTimeMilliseconds), ("InitiatedByUserId", x.InitiatedByUserId),
                    ("CreatedAtUtc", x.CreatedAtUtc), ("StartedAtUtc", x.StartedAtUtc), ("CompletedAtUtc", x.CompletedAtUtc)));
                break;
            }
            case "data-processing":
            {
                AnalyticsRules.ValidateSort(filter, "datasetName", "importCount", "recordsProcessed");
                var query = queries.Imports(filter);
                var grouped = query.GroupBy(x => new { x.DatasetId, x.DatasetName, x.WorkspaceId }).AggregateImports();
                var asc = filter.SortDirection == "asc";
                var sorted = filter.SortBy switch
                {
                    "datasetName" => asc ? grouped.OrderBy(x => x.Key.DatasetName) : grouped.OrderByDescending(x => x.Key.DatasetName),
                    "importCount" => asc ? grouped.OrderBy(x => x.TotalImports) : grouped.OrderByDescending(x => x.TotalImports),
                    _ => asc ? grouped.OrderBy(x => x.TotalRecordsProcessed) : grouped.OrderByDescending(x => x.TotalRecordsProcessed)
                };
                var page = await LoadAsync(sorted.ThenBy(x => x.Key.DatasetId), filter, exportAll, ct);
                var totals = AnalyticsRules.Finish(await query.GroupBy(x => 1).AggregateImports().SingleOrDefaultAsync(ct) ?? new());
                result = Make(reportType, page, ProcessingColumns, ImportSummaryValues(totals), x =>
                {
                    AnalyticsRules.Finish(x);
                    var values = ImportSummaryValues(x);
                    values["DatasetId"] = x.Key.DatasetId;
                    values["DatasetName"] = x.Key.DatasetName;
                    values["WorkspaceId"] = x.Key.WorkspaceId;
                    return values;
                });
                break;
            }
            default:
            {
                var page = await LoadAsync(analytics.SortedWorkspaces(filter), filter, exportAll, ct);
                await analytics.PopulateWorkspaceImportsAsync(page.Items, filter, ct);
                var totals = await analytics.DashboardAsync(filter, ct);
                var summary = ImportSummaryValues(totals.Imports);
                summary["DatasetCount"] = totals.TotalDatasets;
                summary["AverageDatasetQualityScore"] = totals.AverageDatasetQualityScore;
                result = Make(reportType, page, WorkspaceColumns, summary, x =>
                {
                    var values = ImportSummaryValues(x.Imports);
                    values["WorkspaceId"] = x.WorkspaceId; values["WorkspaceName"] = x.WorkspaceName;
                    values["IsActive"] = x.IsActive; values["DatasetCount"] = x.DatasetCount;
                    values["ActiveDatasetCount"] = x.ActiveDatasetCount; values["QualityScore"] = x.QualityScore;
                    values["QualityIssues"] = x.QualityIssues; values["DatasetsRequiringAttention"] = x.DatasetsRequiringAttention;
                    return values;
                });
                break;
            }
        }
        await audit.WriteAsync("Report Generated", "AnalyticsReport", reportType,
            $"Rows={result.Data.Rows.Count}; MatchingRows={result.Pagination.TotalCount}; FromUtc={filter.FromUtc:O}; ToUtc={filter.ToUtc:O}",
            workspaceId: filter.WorkspaceId, cancellationToken: ct);
        return result;
    }

    private async Task<AnalyticsPage<T>> LoadAsync<T>(IQueryable<T> query, AnalyticsQuery filter, bool all, CancellationToken ct)
    {
        if (!all) return await AnalyticsQueries.PageAsync(query, filter, ct);
        var limit = Math.Clamp(options.Value.MaxExportRows, 1, 100000);
        var total = await query.LongCountAsync(ct);
        if (total > limit) TooLarge(limit);
        // The extra row catches growth between count and read. Never silently truncate.
        var rows = await query.Take(limit + 1).ToListAsync(ct);
        if (rows.Count > limit) TooLarge(limit);
        return new(rows, rows.Count, 1, Math.Max(rows.Count, 1));
    }

    private static void TooLarge(int limit) => throw new AnalyticsRequestException(413, "ExportLimitExceeded",
        $"The filtered report exceeds the {limit} row export limit. Narrow the filters or use Scope=page.");

    private static ReportResult Make<T>(string type, AnalyticsPage<T> page, IReadOnlyList<ReportColumn> columns,
        Dictionary<string, object?> summary, Func<T, Dictionary<string, object?>> map)
    {
        summary["MatchingRows"] = page.TotalCount;
        summary["RowsReturned"] = page.Items.Count;
        summary["Timezone"] = "UTC";
        // Whitelist every row field against the public report schema.
        var rows = page.Items.Select(item =>
        {
            var source = map(item);
            return (IReadOnlyDictionary<string, object?>)columns.ToDictionary(x => x.Key, x => source.GetValueOrDefault(x.Key));
        }).ToList();
        return new(new(type, summary, columns, rows), page.Pagination);
    }

    private static Dictionary<string, object?> Values(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(x => x.Key, x => x.Value);

    private static Dictionary<string, object?> ImportSummaryValues(ImportSummary value) =>
        typeof(ImportSummary).GetProperties().ToDictionary(x => x.Name, x => x.GetValue(value));

    private static Dictionary<string, object?> QualitySummaryValues(QualitySummary value) =>
        typeof(QualitySummary).GetProperties().ToDictionary(x => x.Name, x => x.GetValue(value));

    private static ReportColumn C(string key, string header, string type = "string") => new(key, header, type);
    private static readonly ReportColumn[] DatasetColumns =
    [
        C("DatasetId", "Dataset ID"), C("Code", "Dataset Code"), C("DatasetName", "Dataset Name"),
        C("WorkspaceId", "Workspace ID"), C("Workspace", "Workspace"), C("Category", "Category"), C("Owner", "Owner"),
        C("Status", "Dataset Status"), C("CurrentRecordCount", "Current Records", "number"), C("QualityScore", "Quality Score (%)", "number"),
        C("RequiresAttention", "Requires Attention", "boolean"), C("AttentionReasons", "Attention Reasons"),
        C("CreatedAtUtc", "Created At (UTC)", "datetime"), C("UpdatedAtUtc", "Updated At (UTC)", "datetime")
    ];
    private static readonly ReportColumn[] QualityColumns =
    [
        C("DatasetId", "Dataset ID"), C("DatasetName", "Dataset Name"), C("WorkspaceId", "Workspace ID"), C("Workspace", "Workspace"),
        C("ProfileRunId", "Profile Run ID"), C("ProfileCompletedAtUtc", "Profile Completed (UTC)", "datetime"),
        C("QualityScore", "Quality Score (%)", "number"), C("Completeness", "Completeness (%)", "number"), C("Validity", "Validity (%)", "number"),
        C("Uniqueness", "Uniqueness (%)", "number"), C("Consistency", "Consistency (%)", "number"), C("ThresholdStatus", "Threshold Status"),
        C("OverallThreshold", "Applied Overall Threshold (%)", "number"), C("CompletenessThreshold", "Applied Completeness Threshold (%)", "number"),
        C("ValidityThreshold", "Applied Validity Threshold (%)", "number"), C("UniquenessThreshold", "Applied Uniqueness Threshold (%)", "number"),
        C("ConsistencyThreshold", "Applied Consistency Threshold (%)", "number"), C("IssueCount", "Quality Issues", "number"),
        C("InvalidValueCount", "Invalid Values (Cells)", "number"), C("AttentionReasons", "Attention Reasons"), C("ScoringVersion", "Scoring Version")
    ];
    private static readonly ReportColumn[] ImportColumns =
    [
        C("ImportId", "Import ID"), C("DatasetId", "Dataset ID"), C("DatasetName", "Dataset Name"), C("WorkspaceId", "Workspace ID"),
        C("Status", "Import Status"), C("ImportMode", "Import Mode"), C("RecordsAttempted", "Records Attempted", "number"),
        C("RecordsSuccessfullyImported", "Records Successfully Imported", "number"), C("RecordsRejected", "Records Rejected", "number"),
        C("TotalRecordsProcessed", "Known Records Processed", "number"), C("RecordsWithoutFinalOutcome", "Records Without Final Outcome", "number"),
        C("StatisticsComplete", "Statistics Complete", "boolean"), C("ProcessingTimeMilliseconds", "Processing Time (ms)", "number"),
        C("InitiatedByUserId", "Initiated By User ID"), C("CreatedAtUtc", "Created At (UTC)", "datetime"),
        C("StartedAtUtc", "Started At (UTC)", "datetime"), C("CompletedAtUtc", "Completed At (UTC)", "datetime")
    ];
    private static readonly ReportColumn[] MetricColumns =
    [
        C("TotalImports", "Total Imports", "number"), C("SuccessfulImports", "Completed Imports", "number"),
        C("FailedImports", "Failed Imports", "number"), C("CompletedWithErrors", "Completed With Errors", "number"),
        C("CancelledImports", "Cancelled Imports", "number"), C("PendingImports", "Pending Imports", "number"),
        C("RecordsAttempted", "Records Attempted", "number"), C("RecordsSuccessfullyImported", "Records Successfully Imported", "number"),
        C("RecordsRejected", "Records Rejected", "number"), C("TotalRecordsProcessed", "Known Records Processed", "number"),
        C("RecordsWithoutFinalOutcome", "Records Without Final Outcome", "number"),
        C("ImportsWithIncompleteStatistics", "Imports With Incomplete Statistics", "number"),
        C("ImportSuccessRate", "Import Success Rate (%)", "number"), C("RecordSuccessRate", "Record Success Rate (%)", "number"),
        C("AverageProcessingTimeMilliseconds", "Average Processing Time (ms)", "number")
    ];
    private static readonly ReportColumn[] ProcessingColumns =
        [C("DatasetId", "Dataset ID"), C("DatasetName", "Dataset Name"), C("WorkspaceId", "Workspace ID"), .. MetricColumns];
    private static readonly ReportColumn[] WorkspaceColumns =
    [
        C("WorkspaceId", "Workspace ID"), C("WorkspaceName", "Workspace"), C("IsActive", "Active Workspace", "boolean"),
        C("DatasetCount", "Dataset Count", "number"), C("ActiveDatasetCount", "Active Datasets", "number"),
        C("QualityScore", "Average Dataset Quality Score (%)", "number"), C("QualityIssues", "Quality Issues", "number"),
        C("DatasetsRequiringAttention", "Datasets Requiring Attention", "number"), .. MetricColumns
    ];
}
