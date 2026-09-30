using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IDataQualityProfileService
{
    Task<ServiceResult<StartQualityProfileResponse>> StartManualAsync(Guid datasetId, CancellationToken ct);
    Task<Guid?> QueueAutomaticAsync(Guid datasetId, Guid importId, Guid userId, Guid workspaceId, CancellationToken ct);
    Task<ServiceResult<QualityProfileResponse>> GetAsync(Guid profileRunId, CancellationToken ct);
    Task<ServiceResult<QualityProfileResponse>> LatestAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<PagedResponse<QualityProfileResponse>>> HistoryAsync(Guid datasetId, Guid? importId, int page, int pageSize, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<ColumnQualityMetricResponse>>> ColumnsAsync(Guid profileRunId, CancellationToken ct);
    Task<ServiceResult<PagedResponse<QualityIssueResponse>>> IssuesAsync(Guid profileRunId, string? issueType, int page, int pageSize, CancellationToken ct);
    Task<ServiceResult<PagedResponse<QualityIssueResponse>>> DatasetIssuesAsync(Guid datasetId, Guid? importId, string? issueType, int page, int pageSize, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<QualityTrendPointResponse>>> TrendAsync(Guid datasetId, int limit, CancellationToken ct);
    Task<ServiceResult<QualityComparisonResponse>> CompareAsync(Guid datasetId, Guid leftProfileRunId, Guid rightProfileRunId, CancellationToken ct);
    Task<ServiceResult<QualityDashboardResponse>> DashboardAsync(Guid datasetId, int issueLimit, int trendLimit, CancellationToken ct);
    Task<ServiceResult<bool>> CancelAsync(Guid profileRunId, CancellationToken ct);
    Task<ServiceResult<QualityThresholdResponse>> GetThresholdAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<QualityThresholdResponse>> UpsertThresholdAsync(Guid datasetId, UpsertQualityThresholdRequest request, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteThresholdAsync(Guid datasetId, CancellationToken ct);
}

public interface IDataQualityProfileProcessor
{
    Task ProcessAsync(Guid profileRunId, CancellationToken hostToken);
}

public interface IQualityProfileCancellationRegistry
{
    CancellationToken Register(Guid profileRunId, CancellationToken hostToken);
    bool Cancel(Guid profileRunId);
    void Unregister(Guid profileRunId);
}
