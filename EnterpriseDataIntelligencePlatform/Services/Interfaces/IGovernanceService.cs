using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IGovernanceService
{
    Task<ServiceResult<DatasetGovernanceResponse>> UpsertAsync(Guid datasetId, UpsertDatasetGovernanceRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetGovernanceResponse>> GetAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<DatasetGovernanceResponse>> UpdateClassificationAsync(Guid datasetId, UpdateClassificationRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetGovernanceResponse>> AssignUsersAsync(Guid datasetId, AssignGovernanceUsersRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetCertificationResponse>> SubmitCertificationAsync(Guid datasetId, SubmitCertificationRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetCertificationResponse>> ReviewCertificationAsync(Guid certificationId, ReviewCertificationRequest request, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<DatasetCertificationResponse>>> CertificationHistoryAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<DatasetAccessRequestResponse>> RequestAccessAsync(Guid datasetId, CreateDatasetAccessRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetAccessRequestResponse>> ReviewAccessAsync(Guid requestId, ReviewAccessRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetAccessGrantResponse>> RevokeAccessAsync(Guid accessRequestId, RevokeDatasetAccessRequest request, CancellationToken ct);
    Task<ServiceResult<PagedResponse<DatasetAccessRequestResponse>>> AccessRequestsAsync(AccessRequestSearchRequest request, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<DatasetAccessHistoryResponse>>> AccessHistoryAsync(Guid requestId, CancellationToken ct);
    Task<ServiceResult<PagedResponse<DatasetGovernanceResponse>>> SearchAsync(GovernanceSearchRequest request, CancellationToken ct);
    Task<ServiceResult<GovernanceDashboardResponse>> DashboardAsync(CancellationToken ct);
}
