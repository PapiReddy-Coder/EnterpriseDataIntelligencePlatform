using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IDatasetAccessPolicy
{
    Task<ServiceResult<bool>> AuthorizeAsync(Guid datasetId, string requiredAccessLevel, CancellationToken ct);
    Task<bool> IsWorkspaceAdministratorAsync(CancellationToken ct);
    Task<bool> IsDatasetReviewerAsync(Guid datasetId, CancellationToken ct);
}
