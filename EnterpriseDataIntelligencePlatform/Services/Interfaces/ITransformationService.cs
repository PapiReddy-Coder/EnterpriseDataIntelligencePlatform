using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface ITransformationService
{
    Task<ServiceResult<TransformationConfigurationResponse>> SaveAsync(Guid datasetId, SaveTransformationConfigurationRequest request, CancellationToken ct);
    Task<ServiceResult<TransformationConfigurationResponse>> GetActiveAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<TransformationConfigurationResponse>>> HistoryAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(Guid datasetId, Guid configurationId, CancellationToken ct);
    Task<ServiceResult<TransformationPreviewResponse>> PreviewAsync(Guid importId, int limit, CancellationToken ct);
}
