using EnterpriseDataIntelligencePlatform.Contracts;
using Microsoft.AspNetCore.Http;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IImportService
{
    Task<ServiceResult<FileUploadResponse>> UploadAsync(Guid datasetId, IFormFile file, CancellationToken ct);
    Task<ServiceResult<CreateImportResponse>> CreateAsync(Guid datasetId, CreateImportRequest request, CancellationToken ct);
    Task<ServiceResult<StartImportResponse>> StartAsync(Guid importId, CancellationToken ct);
    Task<ServiceResult<ImportDetailsResponse>> GetAsync(Guid importId, CancellationToken ct);
    Task<ServiceResult<PagedResponse<ImportDetailsResponse>>> HistoryAsync(Guid? datasetId, string? status, int page, int pageSize, CancellationToken ct);
    Task<ServiceResult<PagedResponse<ImportErrorResponse>>> ErrorsAsync(Guid importId, int page, int pageSize, CancellationToken ct);
    Task<ServiceResult<bool>> CancelAsync(Guid importId, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<DatasetColumnResponse>>> GetSchemaAsync(Guid datasetId, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<DatasetColumnResponse>>> UpdateKeyColumnsAsync(Guid datasetId, UpdateDatasetKeyColumnsRequest request, CancellationToken ct);
}
