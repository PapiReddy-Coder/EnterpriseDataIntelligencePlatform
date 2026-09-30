using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public sealed record ServiceResult<T>(bool Succeeded, T? Value = default, string? Error = null, int StatusCode = 200)
{
    public static ServiceResult<T> Success(T value, int statusCode = 200) => new(true, value, null, statusCode);
    public static ServiceResult<T> Failure(string error, int statusCode) => new(false, default, error, statusCode);
}

public interface IDatasetService
{
    Task<ServiceResult<DatasetDetailsResponse>> RegisterAsync(RegisterDatasetRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<DatasetDetailsResponse>> UpdateAsync(Guid id, UpdateDatasetRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<DatasetDetailsResponse>> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponse<DatasetCatalogItemResponse>>> SearchAsync(DatasetSearchRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ActivateAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ArchiveAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RestoreArchivedAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> SoftDeleteAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RecoverAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<DatasetVersionHistoryResponse>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceResult<DatasetVersionResponse>> GetVersionAsync(Guid id, int versionNumber, CancellationToken cancellationToken = default);
}
