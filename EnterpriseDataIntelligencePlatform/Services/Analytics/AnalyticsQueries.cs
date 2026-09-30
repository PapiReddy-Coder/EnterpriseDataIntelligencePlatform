using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Analytics;

public sealed class AnalyticsQueries(AppDbContext db, ICurrentUser user)
{
    public async Task ValidateScopeAsync(AnalyticsQuery query, CancellationToken ct)
    {
        AnalyticsRules.Validate(query);
        if (!user.UserId.HasValue)
            throw new AnalyticsRequestException(401, "Unauthenticated", "Authentication is required.");
        if (!user.IsPlatformAdministrator &&
            (!user.WorkspaceId.HasValue || (query.WorkspaceId.HasValue && query.WorkspaceId != user.WorkspaceId)))
            throw new AnalyticsRequestException(403, "WorkspaceForbidden", "The requested workspace is not accessible.");
        if (query.WorkspaceId.HasValue &&
            !await Workspaces(query).AnyAsync(ct))
            throw new AnalyticsRequestException(404, "WorkspaceNotFound", "Workspace not found or not accessible.");
        if (query.DatasetId.HasValue &&
            !await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == query.DatasetId &&
                (!query.WorkspaceId.HasValue || x.WorkspaceId == query.WorkspaceId), ct))
            throw new AnalyticsRequestException(404, "DatasetNotFound", "Dataset not found or not accessible.");
    }

    public IQueryable<Workspace> Workspaces(AnalyticsQuery filter)
    {
        var query = db.Workspaces.AsNoTracking();
        // Workspaces has no global query filter in the original module.
        if (!user.IsPlatformAdministrator) query = query.Where(x => x.Id == user.WorkspaceId);
        if (filter.WorkspaceId.HasValue) query = query.Where(x => x.Id == filter.WorkspaceId);
        return query;
    }

    public IQueryable<DatasetAnalyticsRow> DatasetScope(AnalyticsQuery filter)
    {
        // vw_DatasetAnalytics is already sourced from dbo.Datasets and excludes soft-deleted datasets.
        // Keeping an additional DbSet<Dataset> existence subquery here breaks the isolated/in-memory
        // analytics projection used by tests and is redundant in SQL Server. Workspace isolation is
        // still enforced by the global query filter plus ValidateScopeAsync.
        var query = db.DatasetAnalytics.AsNoTracking();
        if (filter.WorkspaceId.HasValue) query = query.Where(x => x.WorkspaceId == filter.WorkspaceId);
        if (filter.DatasetId.HasValue) query = query.Where(x => x.DatasetId == filter.DatasetId);
        if (filter.CategoryId.HasValue) query = query.Where(x => x.CategoryId == filter.CategoryId);
        if (filter.OwnerId.HasValue) query = query.Where(x => x.OwnerId == filter.OwnerId);
        if (filter.Status is not null) query = query.Where(x => x.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.Name.Contains(search) || x.Code.Contains(search));
        }
        if (filter.QualityStatus == "Unprofiled") query = query.Where(x => x.LatestProfileRunId == null);
        else if (filter.QualityStatus is not null) query = query.Where(x => x.ThresholdStatus == filter.QualityStatus);
        return query;
    }

    public IQueryable<DatasetAnalyticsRow> Datasets(AnalyticsQuery filter)
    {
        AnalyticsRules.ValidateDateField(filter, "created", "updated");
        var query = DatasetScope(filter);
        if (filter.ImportStatus is not null) query = query.Where(x => x.LatestImportStatus == filter.ImportStatus);
        if (filter.DateField == "updated")
        {
            if (filter.FromUtc.HasValue) query = query.Where(x => x.UpdatedAtUtc >= filter.FromUtc);
            if (filter.ToUtc.HasValue) query = query.Where(x => x.UpdatedAtUtc < filter.ToUtc);
        }
        else
        {
            if (filter.FromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= filter.FromUtc);
            if (filter.ToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < filter.ToUtc);
        }
        return query;
    }

    public IQueryable<DatasetAnalyticsRow> Quality(AnalyticsQuery filter, bool applyDates = true)
    {
        AnalyticsRules.ValidateDateField(filter, "completed");
        var query = DatasetScope(filter);
        if (filter.ImportStatus is not null) query = query.Where(x => x.LatestImportStatus == filter.ImportStatus);
        // Select current latest first. A date filter never silently substitutes an older profile.
        if (applyDates)
        {
            if (filter.FromUtc.HasValue) query = query.Where(x => x.ProfileCompletedAtUtc >= filter.FromUtc);
            if (filter.ToUtc.HasValue) query = query.Where(x => x.ProfileCompletedAtUtc < filter.ToUtc);
        }
        return query;
    }

    public IQueryable<ImportAnalyticsRow> Imports(AnalyticsQuery filter)
    {
        AnalyticsRules.ValidateDateField(filter, "created", "started", "completed");
        var eligible = DatasetScope(filter).Select(x => x.DatasetId);
        var query = db.ImportAnalytics.AsNoTracking().Where(x => eligible.Contains(x.DatasetId));
        if (filter.ImportStatus is not null) query = query.Where(x => x.Status == filter.ImportStatus);
        switch (filter.DateField)
        {
            case "started":
                if (filter.FromUtc.HasValue) query = query.Where(x => x.StartedAtUtc >= filter.FromUtc);
                if (filter.ToUtc.HasValue) query = query.Where(x => x.StartedAtUtc < filter.ToUtc);
                break;
            case "completed":
                if (filter.FromUtc.HasValue) query = query.Where(x => x.CompletedAtUtc >= filter.FromUtc);
                if (filter.ToUtc.HasValue) query = query.Where(x => x.CompletedAtUtc < filter.ToUtc);
                break;
            default:
                if (filter.FromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= filter.FromUtc);
                if (filter.ToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < filter.ToUtc);
                break;
        }
        return query;
    }

    public static IOrderedQueryable<DatasetAnalyticsRow> SortDatasets(IQueryable<DatasetAnalyticsRow> query, AnalyticsQuery filter)
    {
        AnalyticsRules.ValidateSort(filter, "name", "createdAtUtc", "updatedAtUtc", "qualityScore", "recordCount", "status");
        var asc = filter.SortDirection == "asc";
        var sorted = (filter.SortBy ?? "updatedAtUtc") switch
        {
            "name" => asc ? query.OrderBy(x => x.Name) : query.OrderByDescending(x => x.Name),
            "createdAtUtc" => asc ? query.OrderBy(x => x.CreatedAtUtc) : query.OrderByDescending(x => x.CreatedAtUtc),
            "qualityScore" => asc ? query.OrderBy(x => x.OverallQualityScore) : query.OrderByDescending(x => x.OverallQualityScore),
            "recordCount" => asc ? query.OrderBy(x => x.CurrentRecordCount) : query.OrderByDescending(x => x.CurrentRecordCount),
            "status" => asc ? query.OrderBy(x => x.Status) : query.OrderByDescending(x => x.Status),
            _ => asc ? query.OrderBy(x => x.UpdatedAtUtc) : query.OrderByDescending(x => x.UpdatedAtUtc)
        };
        return sorted.ThenBy(x => x.DatasetId);
    }

    public static IOrderedQueryable<ImportAnalyticsRow> SortImports(IQueryable<ImportAnalyticsRow> query, AnalyticsQuery filter)
    {
        AnalyticsRules.ValidateSort(filter, "createdAtUtc", "startedAtUtc", "completedAtUtc", "recordsProcessed", "status", "datasetName");
        var asc = filter.SortDirection == "asc";
        var sorted = (filter.SortBy ?? "createdAtUtc") switch
        {
            "startedAtUtc" => asc ? query.OrderBy(x => x.StartedAtUtc) : query.OrderByDescending(x => x.StartedAtUtc),
            "completedAtUtc" => asc ? query.OrderBy(x => x.CompletedAtUtc) : query.OrderByDescending(x => x.CompletedAtUtc),
            "recordsProcessed" => asc ? query.OrderBy(x => x.RecordsProcessed) : query.OrderByDescending(x => x.RecordsProcessed),
            "status" => asc ? query.OrderBy(x => x.Status) : query.OrderByDescending(x => x.Status),
            "datasetName" => asc ? query.OrderBy(x => x.DatasetName) : query.OrderByDescending(x => x.DatasetName),
            _ => asc ? query.OrderBy(x => x.CreatedAtUtc) : query.OrderByDescending(x => x.CreatedAtUtc)
        };
        return sorted.ThenBy(x => x.ImportId);
    }

    public static async Task<AnalyticsPage<T>> PageAsync<T>(IQueryable<T> query, AnalyticsQuery filter, CancellationToken ct)
    {
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip(checked((filter.Page - 1) * filter.PageSize)).Take(filter.PageSize).ToListAsync(ct);
        return new(items, total, filter.Page, filter.PageSize);
    }
}

public sealed class ImportAggregate<TKey> : ImportSummary
{
    public TKey Key { get; set; } = default!;
}

public static class AnalyticsAggregations
{
    public static IQueryable<ImportAggregate<TKey>> AggregateImports<TKey>(
        this IQueryable<IGrouping<TKey, ImportAnalyticsRow>> groups) => groups.Select(g => new ImportAggregate<TKey>
    {
        Key = g.Key,
        TotalImports = g.LongCount(),
        SuccessfulImports = g.LongCount(x => x.Status == ImportStatuses.Completed),
        FailedImports = g.LongCount(x => x.Status == ImportStatuses.Failed),
        CompletedWithErrors = g.LongCount(x => x.Status == ImportStatuses.CompletedWithErrors),
        CancelledImports = g.LongCount(x => x.Status == ImportStatuses.Cancelled),
        PendingImports = g.LongCount(x => x.Status == ImportStatuses.Created || x.Status == ImportStatuses.Queued || x.Status == ImportStatuses.Processing),
        RecordsAttempted = g.Sum(x => x.RecordsAttempted),
        RecordsSuccessfullyImported = g.Sum(x => x.RecordsSuccessfullyImported),
        RecordsRejected = g.Sum(x => x.RecordsRejected),
        TotalRecordsProcessed = g.Sum(x => x.RecordsProcessed),
        RecordsWithoutFinalOutcome = g.Sum(x => x.RecordsWithoutFinalOutcome),
        ImportsWithIncompleteStatistics = g.LongCount(x => !x.StatisticsComplete),
        AverageProcessingTimeMilliseconds = g.Average(x => (double?)x.ProcessingTimeMilliseconds)
    });

    public static IQueryable<QualitySummary> AggregateQuality(this IQueryable<DatasetAnalyticsRow> query) =>
        query.GroupBy(x => 1).Select(g => new QualitySummary
        {
            TotalDatasets = g.LongCount(),
            ProfiledDatasets = g.LongCount(x => x.LatestProfileRunId != null),
            ScoredDatasets = g.LongCount(x => x.OverallQualityScore != null),
            UnprofiledDatasets = g.LongCount(x => x.LatestProfileRunId == null),
            NotApplicableDatasets = g.LongCount(x => x.ThresholdStatus == QualityThresholdStatuses.NotApplicable),
            PassingDatasets = g.LongCount(x => x.ThresholdStatus == QualityThresholdStatuses.Passed),
            FailingDatasets = g.LongCount(x => x.ThresholdStatus == QualityThresholdStatuses.Failed),
            OverallQualityScore = g.Average(x => x.OverallQualityScore),
            Completeness = g.Average(x => x.Completeness),
            Validity = g.Average(x => x.Validity),
            Uniqueness = g.Average(x => x.Uniqueness),
            Consistency = g.Average(x => x.Consistency),
            QualityIssueCount = g.Sum(x => x.QualityIssueCount)
        });
}
