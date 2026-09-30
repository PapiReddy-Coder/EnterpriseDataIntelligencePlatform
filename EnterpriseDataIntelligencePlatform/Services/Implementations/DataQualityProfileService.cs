using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class DataQualityProfileService(
    AppDbContext db,
    ICurrentUser currentUser,
    IBackgroundJobQueue queue,
    IQualityProfileCancellationRegistry cancellations,
    IAuditService audit,
    ILineageCaptureService lineage,
    IDatasetAccessPolicy datasetAccess) : IDataQualityProfileService
{
    public async Task<ServiceResult<StartQualityProfileResponse>> StartManualAsync(Guid datasetId, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Write, ct);
        if (!accessResult.Succeeded) return ServiceResult<StartQualityProfileResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        var dataset = await db.Datasets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == datasetId, ct);
        if (dataset is null)
            return ServiceResult<StartQualityProfileResponse>.Failure("Dataset not found or not accessible.", 404);
        if (!currentUser.UserId.HasValue)
            return ServiceResult<StartQualityProfileResponse>.Failure("Authenticated user context is required.", 401);

        var run = CreateRun(dataset.Id, dataset.WorkspaceId, null, currentUser.UserId, QualityProfileTriggers.Manual);
        db.DataQualityProfileRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await lineage.CaptureQualityProfileAsync(run.Id, ct);
        await queue.EnqueueAsync(new BackgroundJob(BackgroundJobTypes.QualityProfile, run.Id), ct);
        await audit.WriteAsync("Profiling Started", "DataQualityProfileRun", run.Id.ToString(),
            $"DatasetId={dataset.Id}; Trigger=Manual; Status=Queued", userId: currentUser.UserId,
            workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        return ServiceResult<StartQualityProfileResponse>.Success(
            new(run.Id, run.Status, run.TriggerType, run.QueuedAtUtc), 202);
    }

    public async Task<Guid?> QueueAutomaticAsync(
        Guid datasetId, Guid importId, Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var eligible = await db.DataImports.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.Id == importId && x.DatasetId == datasetId && x.WorkspaceId == workspaceId &&
            (x.Status == ImportStatuses.Completed || x.Status == ImportStatuses.CompletedWithErrors), ct);
        if (!eligible) return null;

        var existing = await db.DataQualityProfileRuns.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.ImportId == importId && x.TriggerType == QualityProfileTriggers.Automatic, ct);
        if (existing is not null) return existing.Id;

        var run = CreateRun(datasetId, workspaceId, importId, userId, QualityProfileTriggers.Automatic);
        db.DataQualityProfileRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await lineage.CaptureQualityProfileAsync(run.Id, ct);
        await queue.EnqueueAsync(new BackgroundJob(BackgroundJobTypes.QualityProfile, run.Id), ct);
        await audit.WriteAsync("Profiling Started", "DataQualityProfileRun", run.Id.ToString(),
            $"DatasetId={datasetId}; ImportId={importId}; Trigger=Automatic; Status=Queued",
            userId: userId, workspaceId: workspaceId, cancellationToken: ct);
        return run.Id;
    }

    public async Task<ServiceResult<QualityProfileResponse>> GetAsync(Guid profileRunId, CancellationToken ct)
    {
        var denied = await CheckProfileAccessAsync(profileRunId, DatasetAccessLevels.Read, ct);
        if (denied is not null) return ServiceResult<QualityProfileResponse>.Failure(denied.Value.Error, denied.Value.StatusCode);
        var run = await db.DataQualityProfileRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == profileRunId, ct);
        return run is null
            ? ServiceResult<QualityProfileResponse>.Failure("Quality profile was not found or is not accessible.", 404)
            : ServiceResult<QualityProfileResponse>.Success(await ToResponseAsync(run, ct));
    }

    public async Task<ServiceResult<QualityProfileResponse>> LatestAsync(Guid datasetId, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<QualityProfileResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        if (!await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == datasetId, ct))
            return ServiceResult<QualityProfileResponse>.Failure("Dataset not found or not accessible.", 404);
        var run = await db.DataQualityProfileRuns.AsNoTracking()
            .Where(x => x.DatasetId == datasetId && x.Status == QualityProfileStatuses.Completed)
            .OrderByDescending(x => x.CompletedAtUtc).FirstOrDefaultAsync(ct);
        return run is null
            ? ServiceResult<QualityProfileResponse>.Failure("No completed quality profile exists for this dataset.", 404)
            : ServiceResult<QualityProfileResponse>.Success(await ToResponseAsync(run, ct));
    }

    public async Task<ServiceResult<PagedResponse<QualityProfileResponse>>> HistoryAsync(
        Guid datasetId, Guid? importId, int page, int pageSize, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<PagedResponse<QualityProfileResponse>>.Failure(accessResult.Error!, accessResult.StatusCode);
        if (!await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == datasetId, ct))
            return ServiceResult<PagedResponse<QualityProfileResponse>>.Failure("Dataset not found or not accessible.", 404);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.DataQualityProfileRuns.AsNoTracking().Where(x => x.DatasetId == datasetId);
        if (importId.HasValue) query = query.Where(x => x.ImportId == importId);
        var total = await query.CountAsync(ct);
        var runs = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var responses = new List<QualityProfileResponse>(runs.Count);
        foreach (var run in runs) responses.Add(await ToResponseAsync(run, ct));
        return ServiceResult<PagedResponse<QualityProfileResponse>>.Success(new(responses, page, pageSize, total));
    }

    public async Task<ServiceResult<IReadOnlyList<ColumnQualityMetricResponse>>> ColumnsAsync(Guid profileRunId, CancellationToken ct)
    {
        var denied = await CheckProfileAccessAsync(profileRunId, DatasetAccessLevels.Read, ct);
        if (denied is not null) return ServiceResult<IReadOnlyList<ColumnQualityMetricResponse>>.Failure(denied.Value.Error, denied.Value.StatusCode);
        if (!await db.DataQualityProfileRuns.AsNoTracking().AnyAsync(x => x.Id == profileRunId, ct))
            return ServiceResult<IReadOnlyList<ColumnQualityMetricResponse>>.Failure("Quality profile was not found or is not accessible.", 404);
        var entities = await db.DataQualityColumnMetrics.AsNoTracking().Where(x => x.ProfileRunId == profileRunId)
            .OrderBy(x => x.DatasetColumn.Ordinal).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<ColumnQualityMetricResponse>>.Success(entities.Select(ToColumnResponse).ToList());
    }

    public async Task<ServiceResult<PagedResponse<QualityIssueResponse>>> IssuesAsync(
        Guid profileRunId, string? issueType, int page, int pageSize, CancellationToken ct)
    {
        var denied = await CheckProfileAccessAsync(profileRunId, DatasetAccessLevels.Read, ct);
        if (denied is not null) return ServiceResult<PagedResponse<QualityIssueResponse>>.Failure(denied.Value.Error, denied.Value.StatusCode);
        if (!await db.DataQualityProfileRuns.AsNoTracking().AnyAsync(x => x.Id == profileRunId, ct))
            return ServiceResult<PagedResponse<QualityIssueResponse>>.Failure("Quality profile was not found or is not accessible.", 404);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.DataQualityIssues.AsNoTracking().Where(x => x.ProfileRunId == profileRunId);
        if (!string.IsNullOrWhiteSpace(issueType)) query = query.Where(x => x.IssueType == issueType);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.ColumnName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new QualityIssueResponse(x.Id, x.ProfileRunId, x.ImportId, x.DatasetRecordId, x.ColumnName, x.IssueType, x.Severity,
                x.ValidationRule, x.Description, x.InvalidValue, x.CreatedAtUtc)).ToListAsync(ct);
        return ServiceResult<PagedResponse<QualityIssueResponse>>.Success(new(items, page, pageSize, total));
    }

    public async Task<ServiceResult<PagedResponse<QualityIssueResponse>>> DatasetIssuesAsync(
        Guid datasetId, Guid? importId, string? issueType, int page, int pageSize, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<PagedResponse<QualityIssueResponse>>.Failure(accessResult.Error!, accessResult.StatusCode);
        if (!await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == datasetId, ct))
            return ServiceResult<PagedResponse<QualityIssueResponse>>.Failure("Dataset not found or not accessible.", 404);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.DataQualityIssues.AsNoTracking().Where(x => x.DatasetId == datasetId);
        if (importId.HasValue) query = query.Where(x => x.ImportId == importId);
        if (!string.IsNullOrWhiteSpace(issueType)) query = query.Where(x => x.IssueType == issueType);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new QualityIssueResponse(x.Id, x.ProfileRunId, x.ImportId, x.DatasetRecordId, x.ColumnName,
                x.IssueType, x.Severity, x.ValidationRule, x.Description, x.InvalidValue, x.CreatedAtUtc)).ToListAsync(ct);
        return ServiceResult<PagedResponse<QualityIssueResponse>>.Success(new(items, page, pageSize, total));
    }

    public async Task<ServiceResult<IReadOnlyList<QualityTrendPointResponse>>> TrendAsync(Guid datasetId, int limit, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<IReadOnlyList<QualityTrendPointResponse>>.Failure(accessResult.Error!, accessResult.StatusCode);
        if (!await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == datasetId, ct))
            return ServiceResult<IReadOnlyList<QualityTrendPointResponse>>.Failure("Dataset not found or not accessible.", 404);
        limit = Math.Clamp(limit, 1, 200);
        var items = await db.DataQualityProfileRuns.AsNoTracking()
            .Where(x => x.DatasetId == datasetId && x.Status == QualityProfileStatuses.Completed && x.CompletedAtUtc != null)
            .OrderByDescending(x => x.CompletedAtUtc).Take(limit)
            .Select(x => new QualityTrendPointResponse(x.Id, x.ImportId, x.TriggerType, x.OverallQualityScore,
                x.CompletenessScore, x.ValidityScore, x.UniquenessScore, x.ConsistencyScore, x.ErrorRate,
                x.ThresholdStatus, x.CompletedAtUtc!.Value)).ToListAsync(ct);
        items.Reverse();
        return ServiceResult<IReadOnlyList<QualityTrendPointResponse>>.Success(items);
    }

    public async Task<ServiceResult<QualityComparisonResponse>> CompareAsync(
        Guid datasetId, Guid leftProfileRunId, Guid rightProfileRunId, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<QualityComparisonResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        var runs = await db.DataQualityProfileRuns.AsNoTracking().Where(x => x.DatasetId == datasetId &&
            (x.Id == leftProfileRunId || x.Id == rightProfileRunId) && x.Status == QualityProfileStatuses.Completed).ToListAsync(ct);
        var left = runs.FirstOrDefault(x => x.Id == leftProfileRunId);
        var right = runs.FirstOrDefault(x => x.Id == rightProfileRunId);
        if (left is null || right is null)
            return ServiceResult<QualityComparisonResponse>.Failure("Both completed profiles must belong to the accessible dataset.", 404);
        var metrics = new[]
        {
            Delta("OverallQualityScore", left.OverallQualityScore, right.OverallQualityScore),
            Delta("Completeness", left.CompletenessScore, right.CompletenessScore),
            Delta("Validity", left.ValidityScore, right.ValidityScore),
            Delta("Uniqueness", left.UniquenessScore, right.UniquenessScore),
            Delta("Consistency", left.ConsistencyScore, right.ConsistencyScore),
            Delta("ErrorRate", left.ErrorRate, right.ErrorRate)
        };
        return ServiceResult<QualityComparisonResponse>.Success(new(datasetId, leftProfileRunId, rightProfileRunId, metrics));
    }

    public async Task<ServiceResult<QualityDashboardResponse>> DashboardAsync(
        Guid datasetId, int issueLimit, int trendLimit, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<QualityDashboardResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        var latestResult = await LatestAsync(datasetId, ct);
        if (!latestResult.Succeeded || latestResult.Value is null)
            return ServiceResult<QualityDashboardResponse>.Failure(latestResult.Error ?? "No profile is available.", latestResult.StatusCode);
        var runId = latestResult.Value.ProfileRunId;
        var columnEntities = await db.DataQualityColumnMetrics.AsNoTracking().Where(x => x.ProfileRunId == runId)
            .OrderBy(x => x.DatasetColumn.Ordinal).ToListAsync(ct);
        var columns = columnEntities.Select(ToColumnResponse).ToList();
        issueLimit = Math.Clamp(issueLimit, 1, 20);
        var topIssues = await db.DataQualityIssues.AsNoTracking().Where(x => x.ProfileRunId == runId)
            .GroupBy(x => x.IssueType).Select(x => new QualityIssueSummaryResponse(x.Key, x.Count(),
                x.Where(y => y.DatasetRecordId != null).Select(y => y.DatasetRecordId).Distinct().Count()))
            .OrderByDescending(x => x.Count).Take(issueLimit).ToListAsync(ct);
        var trend = await TrendAsync(datasetId, trendLimit, ct);
        var failed = latestResult.Value.Dimensions.Where(x => x.Passed == false).ToList();
        return ServiceResult<QualityDashboardResponse>.Success(new(latestResult.Value, columns, topIssues,
            trend.Value ?? [], failed));
    }

    public async Task<ServiceResult<bool>> CancelAsync(Guid profileRunId, CancellationToken ct)
    {
        var denied = await CheckProfileAccessAsync(profileRunId, DatasetAccessLevels.Write, ct);
        if (denied is not null) return ServiceResult<bool>.Failure(denied.Value.Error, denied.Value.StatusCode);
        var run = await db.DataQualityProfileRuns.FirstOrDefaultAsync(x => x.Id == profileRunId, ct);
        if (run is null) return ServiceResult<bool>.Failure("Quality profile was not found or is not accessible.", 404);
        var auditAction = "Profiling Cancellation Requested";
        if (run.Status == QualityProfileStatuses.Queued)
        {
            run.Status = QualityProfileStatuses.Cancelled;
            run.CancellationRequested = true;
            run.CompletedAtUtc = DateTime.UtcNow;
            auditAction = "Profiling Cancelled";
        }
        else if (run.Status == QualityProfileStatuses.Processing)
        {
            run.CancellationRequested = true;
            cancellations.Cancel(profileRunId);
        }
        else return ServiceResult<bool>.Failure("Only queued or processing profiles can be cancelled.", 409);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(auditAction, "DataQualityProfileRun", profileRunId.ToString(),
            $"DatasetId={run.DatasetId}", userId: currentUser.UserId, workspaceId: run.WorkspaceId,
            cancellationToken: ct);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<QualityThresholdResponse>> GetThresholdAsync(Guid datasetId, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!accessResult.Succeeded) return ServiceResult<QualityThresholdResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        if (!await db.Datasets.AsNoTracking().AnyAsync(x => x.Id == datasetId, ct))
            return ServiceResult<QualityThresholdResponse>.Failure("Dataset not found or not accessible.", 404);
        var threshold = await db.DataQualityThresholds.AsNoTracking().FirstOrDefaultAsync(x => x.DatasetId == datasetId, ct);
        return ServiceResult<QualityThresholdResponse>.Success(ToThresholdResponse(datasetId, threshold));
    }

    public async Task<ServiceResult<QualityThresholdResponse>> UpsertThresholdAsync(
        Guid datasetId, UpsertQualityThresholdRequest request, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Write, ct);
        if (!accessResult.Succeeded) return ServiceResult<QualityThresholdResponse>.Failure(accessResult.Error!, accessResult.StatusCode);
        var dataset = await db.Datasets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == datasetId, ct);
        if (dataset is null) return ServiceResult<QualityThresholdResponse>.Failure("Dataset not found or not accessible.", 404);
        if (!currentUser.UserId.HasValue) return ServiceResult<QualityThresholdResponse>.Failure("Authenticated user context is required.", 401);
        var threshold = await db.DataQualityThresholds.FirstOrDefaultAsync(x => x.DatasetId == datasetId, ct);
        var action = threshold is null ? "Created" : "Updated";
        if (threshold is null)
        {
            threshold = new DataQualityThreshold
            {
                DatasetId = datasetId, WorkspaceId = dataset.WorkspaceId,
                CreatedByUserId = currentUser.UserId.Value, UpdatedByUserId = currentUser.UserId.Value
            };
            db.DataQualityThresholds.Add(threshold);
        }
        threshold.MinimumCompleteness = request.MinimumCompleteness;
        threshold.MinimumValidity = request.MinimumValidity;
        threshold.MinimumUniqueness = request.MinimumUniqueness;
        threshold.MinimumConsistency = request.MinimumConsistency;
        threshold.MinimumOverallScore = request.MinimumOverallScore;
        threshold.UpdatedByUserId = currentUser.UserId.Value;
        threshold.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Quality Threshold Configuration Changed", "DataQualityThreshold", threshold.Id.ToString(),
            $"DatasetId={datasetId}; Action={action}; Completeness={request.MinimumCompleteness}; Validity={request.MinimumValidity}; " +
            $"Uniqueness={request.MinimumUniqueness}; Consistency={request.MinimumConsistency}; Overall={request.MinimumOverallScore}",
            userId: currentUser.UserId, workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        return ServiceResult<QualityThresholdResponse>.Success(ToThresholdResponse(datasetId, threshold));
    }

    public async Task<ServiceResult<bool>> DeleteThresholdAsync(Guid datasetId, CancellationToken ct)
    {
        var accessResult = await datasetAccess.AuthorizeAsync(datasetId, DatasetAccessLevels.Write, ct);
        if (!accessResult.Succeeded) return ServiceResult<bool>.Failure(accessResult.Error!, accessResult.StatusCode);
        var threshold = await db.DataQualityThresholds.FirstOrDefaultAsync(x => x.DatasetId == datasetId, ct);
        if (threshold is null) return ServiceResult<bool>.Failure("Dataset-specific quality thresholds were not found.", 404);
        var thresholdId = threshold.Id;
        db.DataQualityThresholds.Remove(threshold);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Quality Threshold Configuration Changed", "DataQualityThreshold", thresholdId.ToString(),
            $"DatasetId={datasetId}; Action=Deleted; System defaults now apply.", userId: currentUser.UserId,
            workspaceId: threshold.WorkspaceId, cancellationToken: ct);
        return ServiceResult<bool>.Success(true);
    }

    private async Task<(string Error, int StatusCode)?> CheckProfileAccessAsync(Guid profileRunId, string level, CancellationToken ct)
    {
        var datasetId = await db.DataQualityProfileRuns.IgnoreQueryFilters().Where(x => x.Id == profileRunId)
            .Select(x => (Guid?)x.DatasetId).SingleOrDefaultAsync(ct);
        if (!datasetId.HasValue) return null;
        var result = await datasetAccess.AuthorizeAsync(datasetId.Value, level, ct);
        return result.Succeeded ? null : (result.Error!, result.StatusCode);
    }

    private static DataQualityProfileRun CreateRun(Guid datasetId, Guid workspaceId, Guid? importId, Guid? userId, string trigger) =>
        new() { DatasetId = datasetId, WorkspaceId = workspaceId, ImportId = importId, RequestedByUserId = userId,
            TriggerType = trigger, Status = QualityProfileStatuses.Queued, QueuedAtUtc = DateTime.UtcNow };

    private async Task<QualityProfileResponse> ToResponseAsync(DataQualityProfileRun run, CancellationToken ct)
    {
        ImportQualityStatisticsResponse? importStatistics = null;
        if (run.ImportId.HasValue)
        {
            var import = await db.DataImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == run.ImportId, ct);
            if (import is not null)
            {
                var valid = Math.Max(0, import.TotalRecords - import.RejectedRecords);
                importStatistics = new(import.Id, import.TotalRecords, valid, import.RejectedRecords, import.ErrorCount,
                    QualityCalculations.Percentage(valid, import.TotalRecords),
                    QualityCalculations.Percentage(import.RejectedRecords, import.TotalRecords));
            }
        }
        var dimensions = Dimensions(run);
        return new(run.Id, run.DatasetId, run.ImportId, run.TriggerType, run.Status, run.RetryCount, run.TotalRecords,
            run.CompletenessScore, run.ValidityScore, run.UniquenessScore, run.ConsistencyScore, run.ErrorRate,
            run.OverallQualityScore, run.ThresholdStatus, run.ScoringVersion, dimensions, importStatistics,
            run.FailureMessage, run.QueuedAtUtc, run.StartedAtUtc, run.CompletedAtUtc);
    }

    private static IReadOnlyList<QualityDimensionResponse> Dimensions(DataQualityProfileRun run) =>
    [
        Dimension("Completeness", run.CompletenessScore, run.AppliedCompletenessThreshold),
        Dimension("Validity", run.ValidityScore, run.AppliedValidityThreshold),
        Dimension("Uniqueness", run.UniquenessScore, run.AppliedUniquenessThreshold),
        Dimension("Consistency", run.ConsistencyScore, run.AppliedConsistencyThreshold),
        Dimension("Overall", run.OverallQualityScore, run.AppliedOverallThreshold)
    ];

    private static QualityDimensionResponse Dimension(string name, decimal? score, decimal threshold) =>
        new(name, score, threshold, score.HasValue ? score.Value >= threshold : null);

    private static ColumnQualityMetricResponse ToColumnResponse(DataQualityColumnMetric x) =>
        new(x.DatasetColumnId, x.ColumnName, x.DataType, x.IsRequired, x.IsKey, x.TotalRecords, x.NonNullRecords,
            x.NullCount, x.NullPercentage, x.UniqueCount, x.DistinctCount, x.DuplicateCount, x.InvalidCount,
            x.CompletenessScore, x.ValidityScore, x.UniquenessScore, x.ConsistencyScore, x.QualityScore,
            x.MinimumValue, x.MaximumValue, x.AverageValue);

    private static QualityMetricDeltaResponse Delta(string name, decimal? before, decimal? after) =>
        new(name, before, after, before.HasValue && after.HasValue ? Math.Round(after.Value - before.Value, 2) : null);

    private static QualityThresholdResponse ToThresholdResponse(Guid datasetId, DataQualityThreshold? threshold) =>
        threshold is null
            ? new(datasetId, QualityDefaults.CompletenessThreshold, QualityDefaults.ValidityThreshold,
                QualityDefaults.UniquenessThreshold, QualityDefaults.ConsistencyThreshold, QualityDefaults.OverallThreshold,
                true, null)
            : new(datasetId, threshold.MinimumCompleteness, threshold.MinimumValidity, threshold.MinimumUniqueness,
                threshold.MinimumConsistency, threshold.MinimumOverallScore, false, threshold.UpdatedAtUtc);
}
