using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EnterpriseDataIntelligencePlatform.Services.Analytics;

public interface IAnalyticsService
{
    Task<DashboardSummary> DashboardAsync(AnalyticsQuery query, CancellationToken ct);
    Task<AnalyticsPage<DatasetAnalyticsRow>> DatasetsAsync(AnalyticsQuery query, bool quality, CancellationToken ct);
    Task<AnalyticsPage<DatasetDistribution>> DistributionAsync(AnalyticsQuery query, string groupBy, CancellationToken ct);
    Task<AnalyticsPage<AttentionDataset>> AttentionAsync(AnalyticsQuery query, CancellationToken ct);
    Task<QualitySummary> QualitySummaryAsync(AnalyticsQuery query, CancellationToken ct);
    Task<AnalyticsPage<QualityIssueAggregate>> IssuesAsync(AnalyticsQuery query, string? issueType, string? severity, CancellationToken ct);
    Task<AnalyticsPage<QualityTrendPoint>> QualityTrendAsync(TrendQuery query, CancellationToken ct);
    Task<ImportSummary> ImportSummaryAsync(AnalyticsQuery query, CancellationToken ct);
    Task<AnalyticsPage<ImportAnalyticsRow>> ImportsAsync(AnalyticsQuery query, CancellationToken ct);
    Task<AnalyticsPage<DatasetImportActivity>> ImportsByDatasetAsync(AnalyticsQuery query, CancellationToken ct);
    Task<AnalyticsPage<ImportTrendPoint>> ImportTrendAsync(TrendQuery query, CancellationToken ct);
    Task<AnalyticsPage<WorkspaceSummary>> WorkspacesAsync(AnalyticsQuery query, CancellationToken ct);
    Task<WorkspaceSummary> WorkspaceAsync(Guid workspaceId, AnalyticsQuery query, CancellationToken ct);
}

public sealed class AnalyticsService(AppDbContext db, AnalyticsQueries queries, ICurrentUser user,
    IMemoryCache cache, IOptions<AnalyticsOptions> options) : IAnalyticsService
{
    public async Task<DashboardSummary> DashboardAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        AnalyticsRules.ValidateDateField(filter, "created", "started", "completed");
        var cacheKey = $"analytics:{user.UserId}:{user.WorkspaceId}:{user.IsPlatformAdministrator}:{JsonSerializer.Serialize(filter)}";
        var seconds = Math.Clamp(options.Value.SummaryCacheSeconds, 0, 300);
        if (seconds > 0 && cache.TryGetValue<DashboardSummary>(cacheKey, out var cached) && cached is not null)
            return cached;
        var datasets = queries.DatasetScope(filter);
        var counts = await datasets.GroupBy(x => 1).Select(g => new
        {
            Total = g.LongCount(),
            Active = g.LongCount(x => x.Status == DatasetStatuses.Active),
            Archived = g.LongCount(x => x.Status == DatasetStatuses.Archived),
            Draft = g.LongCount(x => x.Status == DatasetStatuses.Draft),
            Attention = g.LongCount(x => x.RequiresAttention)
        }).SingleOrDefaultAsync(ct);
        var quality = AnalyticsRules.Finish(await datasets.AggregateQuality().SingleOrDefaultAsync(ct) ?? new());
        var imports = await ImportSummaryAsync(filter, ct);
        var result = new DashboardSummary(counts?.Total ?? 0, counts?.Active ?? 0, counts?.Archived ?? 0,
            counts?.Draft ?? 0, counts?.Attention ?? 0, quality.OverallQualityScore, imports, quality);
        if (seconds > 0) cache.Set(cacheKey, result, TimeSpan.FromSeconds(seconds));
        return result;
    }

    public async Task<AnalyticsPage<DatasetAnalyticsRow>> DatasetsAsync(AnalyticsQuery filter, bool quality, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        var rows = quality ? queries.Quality(filter) : queries.Datasets(filter);
        return await AnalyticsQueries.PageAsync(AnalyticsQueries.SortDatasets(rows, filter), filter, ct);
    }

    public async Task<AnalyticsPage<DatasetDistribution>> DistributionAsync(AnalyticsQuery filter, string groupBy, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        var rows = queries.Datasets(filter);
        IQueryable<DatasetDistribution> groups = groupBy switch
        {
            "status" => rows.GroupBy(x => x.Status).Select(g => new DatasetDistribution { Key = g.Key, Label = g.Key, DatasetCount = g.LongCount() }),
            "category" => rows.GroupBy(x => new { x.CategoryId, x.CategoryName })
                .Select(g => new DatasetDistribution { Key = g.Key.CategoryId.ToString(), Label = g.Key.CategoryName, DatasetCount = g.LongCount() }),
            "workspace" => rows.GroupBy(x => new { x.WorkspaceId, x.WorkspaceName })
                .Select(g => new DatasetDistribution { Key = g.Key.WorkspaceId.ToString(), Label = g.Key.WorkspaceName, DatasetCount = g.LongCount() }),
            "owner" => rows.GroupBy(x => new { x.OwnerId, x.OwnerName })
                .Select(g => new DatasetDistribution { Key = g.Key.OwnerId.ToString(), Label = g.Key.OwnerName, DatasetCount = g.LongCount() }),
            _ => throw new AnalyticsRequestException(400, "InvalidParameters", "GroupBy must be status, category, workspace, or owner.")
        };
        AnalyticsRules.ValidateSort(filter, "count", "label");
        var asc = filter.SortDirection == "asc";
        var sorted = filter.SortBy == "label"
            ? (asc ? groups.OrderBy(x => x.Label) : groups.OrderByDescending(x => x.Label))
            : (asc ? groups.OrderBy(x => x.DatasetCount) : groups.OrderByDescending(x => x.DatasetCount));
        return await AnalyticsQueries.PageAsync(sorted.ThenBy(x => x.Key), filter, ct);
    }

    public async Task<AnalyticsPage<AttentionDataset>> AttentionAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        var rows = queries.Datasets(filter).Where(x => x.RequiresAttention);
        var page = await AnalyticsQueries.PageAsync(AnalyticsQueries.SortDatasets(rows, filter), filter, ct);
        return new(page.Items.Select(x => new AttentionDataset(x.DatasetId, x.Name, x.WorkspaceId,
            x.OverallQualityScore, x.LatestImportStatus, AnalyticsRules.AttentionReasons(x))).ToList(),
            page.TotalCount, page.Page, page.PageSize);
    }

    public async Task<QualitySummary> QualitySummaryAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        return AnalyticsRules.Finish(await queries.Quality(filter).AggregateQuality().SingleOrDefaultAsync(ct) ?? new());
    }

    public async Task<AnalyticsPage<QualityIssueAggregate>> IssuesAsync(
        AnalyticsQuery filter, string? issueType, string? severity, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        if (issueType?.Length > 50 || severity?.Length > 20) AnalyticsRules.Invalid("IssueType or Severity is too long.");
        var latestIds = queries.Quality(filter).Where(x => x.LatestProfileRunId != null).Select(x => x.LatestProfileRunId);
        var issues = db.DataQualityIssues.AsNoTracking().Where(x => latestIds.Contains(x.ProfileRunId));
        if (issueType is not null) issues = issues.Where(x => x.IssueType == issueType);
        if (severity is not null) issues = issues.Where(x => x.Severity == severity);
        var groups = issues.GroupBy(x => new { x.IssueType, x.Severity })
            .Select(g => new QualityIssueAggregate { IssueType = g.Key.IssueType, Severity = g.Key.Severity,
                IssueCount = g.LongCount(), AffectedDatasets = g.Select(x => x.DatasetId).Distinct().LongCount() });
        AnalyticsRules.ValidateSort(filter, "count", "issueType");
        var asc = filter.SortDirection == "asc";
        var sorted = filter.SortBy == "issueType"
            ? (asc ? groups.OrderBy(x => x.IssueType) : groups.OrderByDescending(x => x.IssueType))
            : (asc ? groups.OrderBy(x => x.IssueCount) : groups.OrderByDescending(x => x.IssueCount));
        return await AnalyticsQueries.PageAsync(sorted.ThenBy(x => x.IssueType).ThenBy(x => x.Severity), filter, ct);
    }

    // Parameters remain SQL parameters; only a closed-list column identifier is interpolated.
    public IQueryable<QualityTrendPoint> QualityTrendQuery(TrendQuery filter, DateTime from, DateTime to)
    {
        var bucket = filter.Grouping switch
        {
            "daily" => "DayUtc", "weekly" => "WeekUtc", "monthly" => "MonthUtc",
            _ => throw new AnalyticsRequestException(400, "InvalidParameters", "Invalid grouping.")
        };
        var workspace = user.IsPlatformAdministrator ? filter.WorkspaceId : user.WorkspaceId;
        var sql = $"""
            SELECT ranked.* FROM (
                SELECT source.*, ROW_NUMBER() OVER (
                    PARTITION BY DatasetId, {bucket}
                    ORDER BY CompletedAtUtc DESC, ProfileCreatedAtUtc DESC, ProfileRunId DESC) AS SequenceNumber
                FROM dbo.vw_QualityTrendSource AS source
                WHERE CompletedAtUtc >= @from AND CompletedAtUtc < @to
                  AND (@workspace IS NULL OR WorkspaceId = @workspace)
            ) AS ranked WHERE SequenceNumber = 1
            """;
        var source = db.QualityTrendSource.FromSqlRaw(sql,
            new SqlParameter("@from", System.Data.SqlDbType.DateTime2) { Value = from },
            new SqlParameter("@to", System.Data.SqlDbType.DateTime2) { Value = to },
            new SqlParameter("@workspace", System.Data.SqlDbType.UniqueIdentifier) { Value = (object?)workspace ?? DBNull.Value })
            .AsNoTracking();
        var ids = queries.DatasetScope(filter).Select(x => x.DatasetId);
        source = source.Where(x => ids.Contains(x.DatasetId));
        var groups = filter.Grouping switch
        {
            "weekly" => source.GroupBy(x => x.WeekUtc),
            "monthly" => source.GroupBy(x => x.MonthUtc),
            _ => source.GroupBy(x => x.DayUtc)
        };
        return groups.Select(g => new QualityTrendPoint
        {
            BucketUtc = g.Key, DatasetCount = g.LongCount(),
            ScoredDatasetCount = g.LongCount(x => x.OverallQualityScore != null),
            OverallQualityScore = g.Average(x => x.OverallQualityScore),
            Completeness = g.Average(x => x.Completeness), Validity = g.Average(x => x.Validity),
            Uniqueness = g.Average(x => x.Uniqueness), Consistency = g.Average(x => x.Consistency)
        });
    }

    public async Task<AnalyticsPage<QualityTrendPoint>> QualityTrendAsync(TrendQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        AnalyticsRules.ValidateDateField(filter, "completed");
        AnalyticsRules.ValidateSort(filter, "bucketUtc");
        if (filter.QualityStatus is not null || filter.ImportStatus is not null)
            AnalyticsRules.Invalid("Quality/import status filters do not apply to historical quality trends.");
        var (from, to) = AnalyticsRules.TrendRange(filter, options.Value.MaxTrendDays, DateTime.UtcNow);
        var query = QualityTrendQuery(filter, from, to);
        var page = await AnalyticsQueries.PageAsync(filter.SortDirection == "asc"
            ? query.OrderBy(x => x.BucketUtc) : query.OrderByDescending(x => x.BucketUtc), filter, ct);
        foreach (var item in page.Items)
        {
            item.BucketUtc = DateTime.SpecifyKind(item.BucketUtc, DateTimeKind.Utc);
            item.OverallQualityScore = AnalyticsRules.Round(item.OverallQualityScore);
            item.Completeness = AnalyticsRules.Round(item.Completeness);
            item.Validity = AnalyticsRules.Round(item.Validity);
            item.Uniqueness = AnalyticsRules.Round(item.Uniqueness);
            item.Consistency = AnalyticsRules.Round(item.Consistency);
        }
        return page;
    }

    public async Task<ImportSummary> ImportSummaryAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        return AnalyticsRules.Finish(await queries.Imports(filter).GroupBy(x => 1).AggregateImports().SingleOrDefaultAsync(ct)
            ?? new ImportAggregate<int>());
    }

    public async Task<AnalyticsPage<ImportAnalyticsRow>> ImportsAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        return await AnalyticsQueries.PageAsync(AnalyticsQueries.SortImports(queries.Imports(filter), filter), filter, ct);
    }

    public async Task<AnalyticsPage<DatasetImportActivity>> ImportsByDatasetAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        AnalyticsRules.ValidateSort(filter, "importCount", "datasetName", "recordsProcessed");
        var groups = queries.Imports(filter).GroupBy(x => new { x.DatasetId, x.DatasetName, x.WorkspaceId }).AggregateImports();
        var asc = filter.SortDirection == "asc";
        var sorted = filter.SortBy switch
        {
            "datasetName" => asc ? groups.OrderBy(x => x.Key.DatasetName) : groups.OrderByDescending(x => x.Key.DatasetName),
            "recordsProcessed" => asc ? groups.OrderBy(x => x.TotalRecordsProcessed) : groups.OrderByDescending(x => x.TotalRecordsProcessed),
            _ => asc ? groups.OrderBy(x => x.TotalImports) : groups.OrderByDescending(x => x.TotalImports)
        };
        var page = await AnalyticsQueries.PageAsync(sorted.ThenBy(x => x.Key.DatasetId), filter, ct);
        return new(page.Items.Select(x => new DatasetImportActivity(x.Key.DatasetId, x.Key.DatasetName,
            x.Key.WorkspaceId, AnalyticsRules.Finish(x))).ToList(), page.TotalCount, page.Page, page.PageSize);
    }

    public IQueryable<ImportAggregate<DateTime>> ImportTrendQuery(TrendQuery filter)
    {
        var query = queries.Imports(filter);
        // Pick the timestamp BEFORE bucketing, including null exclusion.
        var timed = query.Select(x => new { Row = x,
            Time = filter.DateField == "started" ? x.StartedAtUtc :
                   filter.DateField == "completed" ? x.CompletedAtUtc : (DateTime?)x.CreatedAtUtc })
            .Where(x => x.Time != null);
        var monday = new DateTime(1900, 1, 1);
        var grouped = filter.Grouping switch
        {
            "weekly" => timed.GroupBy(x => x.Time!.Value.Date.AddDays(
                -((EF.Functions.DateDiffDay(monday, x.Time.Value) % 7 + 7) % 7)), x => x.Row),
            "monthly" => timed.GroupBy(x => x.Time!.Value.Date.AddDays(1 - x.Time.Value.Day), x => x.Row),
            _ => timed.GroupBy(x => x.Time!.Value.Date, x => x.Row)
        };
        return grouped.AggregateImports();
    }

    public async Task<AnalyticsPage<ImportTrendPoint>> ImportTrendAsync(TrendQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        AnalyticsRules.ValidateSort(filter, "bucketUtc");
        var range = AnalyticsRules.TrendRange(filter, options.Value.MaxTrendDays, DateTime.UtcNow);
        filter.FromUtc = range.From; filter.ToUtc = range.To;
        var groups = ImportTrendQuery(filter);
        var page = await AnalyticsQueries.PageAsync(filter.SortDirection == "asc"
            ? groups.OrderBy(x => x.Key) : groups.OrderByDescending(x => x.Key), filter, ct);
        return new(page.Items.Select(x => new ImportTrendPoint(DateTime.SpecifyKind(x.Key, DateTimeKind.Utc),
            AnalyticsRules.Finish(x))).ToList(), page.TotalCount, page.Page, page.PageSize);
    }

    public IQueryable<WorkspaceSummary> WorkspaceRows(AnalyticsQuery filter)
    {
        var datasets = queries.DatasetScope(filter);
        var workspaces = queries.Workspaces(filter);
        if (filter.DatasetId.HasValue || filter.CategoryId.HasValue || filter.OwnerId.HasValue ||
            filter.Status is not null || filter.QualityStatus is not null || !string.IsNullOrWhiteSpace(filter.Search))
            workspaces = workspaces.Where(w => datasets.Any(d => d.WorkspaceId == w.Id));
        return workspaces.Select(w => new WorkspaceSummary
        {
            WorkspaceId = w.Id, WorkspaceName = w.Name, IsActive = w.IsActive,
            DatasetCount = datasets.LongCount(x => x.WorkspaceId == w.Id),
            ActiveDatasetCount = datasets.LongCount(x => x.WorkspaceId == w.Id && x.Status == DatasetStatuses.Active),
            DatasetsRequiringAttention = datasets.LongCount(x => x.WorkspaceId == w.Id && x.RequiresAttention),
            QualityScore = datasets.Where(x => x.WorkspaceId == w.Id).Average(x => x.OverallQualityScore),
            QualityIssues = datasets.Where(x => x.WorkspaceId == w.Id).Sum(x => (long?)x.QualityIssueCount) ?? 0
        });
    }

    public IOrderedQueryable<WorkspaceSummary> SortedWorkspaces(AnalyticsQuery filter)
    {
        AnalyticsRules.ValidateDateField(filter, "created", "started", "completed");
        AnalyticsRules.ValidateSort(filter, "workspaceName", "datasetCount", "qualityScore");
        var query = WorkspaceRows(filter);
        var asc = filter.SortDirection == "asc";
        var sorted = filter.SortBy switch
        {
            "datasetCount" => asc ? query.OrderBy(x => x.DatasetCount) : query.OrderByDescending(x => x.DatasetCount),
            "qualityScore" => asc ? query.OrderBy(x => x.QualityScore) : query.OrderByDescending(x => x.QualityScore),
            _ => asc ? query.OrderBy(x => x.WorkspaceName) : query.OrderByDescending(x => x.WorkspaceName)
        };
        return sorted.ThenBy(x => x.WorkspaceId);
    }

    public async Task PopulateWorkspaceImportsAsync(IReadOnlyList<WorkspaceSummary> items, AnalyticsQuery filter, CancellationToken ct)
    {
        var ids = items.Select(x => x.WorkspaceId).ToList();
        if (ids.Count == 0) return;
        var grouped = await queries.Imports(filter).Where(x => ids.Contains(x.WorkspaceId))
            .GroupBy(x => x.WorkspaceId).AggregateImports().ToListAsync(ct);
        var byId = grouped.ToDictionary(x => x.Key);
        foreach (var item in items)
        {
            item.QualityScore = AnalyticsRules.Round(item.QualityScore);
            item.Imports = byId.TryGetValue(item.WorkspaceId, out var value) ? AnalyticsRules.Finish(value) : new();
        }
    }

    public async Task<AnalyticsPage<WorkspaceSummary>> WorkspacesAsync(AnalyticsQuery filter, CancellationToken ct)
    {
        await queries.ValidateScopeAsync(filter, ct);
        var page = await AnalyticsQueries.PageAsync(SortedWorkspaces(filter), filter, ct);
        await PopulateWorkspaceImportsAsync(page.Items, filter, ct);
        return page;
    }

    public async Task<WorkspaceSummary> WorkspaceAsync(Guid workspaceId, AnalyticsQuery filter, CancellationToken ct)
    {
        AnalyticsRules.ValidateDateField(filter, "created", "started", "completed");
        if (filter.WorkspaceId.HasValue && filter.WorkspaceId != workspaceId)
            AnalyticsRules.Invalid("Route and query WorkspaceId must match.");
        filter.WorkspaceId = workspaceId;
        await queries.ValidateScopeAsync(filter, ct);
        var result = await WorkspaceRows(filter).SingleOrDefaultAsync(ct);
        if (result is null) throw new AnalyticsRequestException(404, "WorkspaceNotFound", "No workspace matches the supplied filters.");
        await PopulateWorkspaceImportsAsync([result], filter, ct);
        var categories = queries.DatasetScope(filter).GroupBy(x => new { x.CategoryId, x.CategoryName })
            .Select(g => new DatasetDistribution { Key = g.Key.CategoryId.ToString(), Label = g.Key.CategoryName, DatasetCount = g.LongCount() });
        result.DatasetDistributionByCategory = await AnalyticsQueries.PageAsync(
            categories.OrderByDescending(x => x.DatasetCount).ThenBy(x => x.Key), filter, ct);
        return result;
    }
}
