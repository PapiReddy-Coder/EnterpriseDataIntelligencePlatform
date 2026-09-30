using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public sealed record ExportDownload(Stream Stream, string ContentType, string FileName);

public interface IDataSharingService
{
    Task<ServiceResult<ExportRequestResponse>> CreateExportAsync(CreateExportRequest request, CancellationToken ct);
    Task<ServiceResult<ExportRequestResponse>> GetExportAsync(Guid exportId, CancellationToken ct);
    Task<ServiceResult<ExportRequestResponse>> CancelExportAsync(Guid exportId, CancellationToken ct);
    Task<ServiceResult<PagedResponse<ExportHistoryResponse>>> SearchExportsAsync(ExportHistorySearchRequest request, CancellationToken ct);
    Task<ServiceResult<ExportDashboardResponse>> DashboardAsync(CancellationToken ct);
    Task<ServiceResult<ExportDownload>> DownloadExportAsync(Guid exportId, CancellationToken ct);
    Task<ServiceResult<DatasetShareResponse>> ShareDatasetAsync(CreateDatasetShareRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetShareResponse>> UpdateShareAsync(Guid grantId, CreateDatasetShareRequest request, CancellationToken ct);
    Task<ServiceResult<DatasetShareResponse>> RevokeShareAsync(Guid grantId, RevokeDatasetShareRequest request, CancellationToken ct);
    Task<ServiceResult<PagedResponse<DatasetShareHistoryResponse>>> SearchSharesAsync(ShareHistorySearchRequest request, CancellationToken ct);
    Task ProcessExportAsync(Guid exportId, CancellationToken ct);
    Task CleanupExpiredExportsAsync(CancellationToken ct);
}
