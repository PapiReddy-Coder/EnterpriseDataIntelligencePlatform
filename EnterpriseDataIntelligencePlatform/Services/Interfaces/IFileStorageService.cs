using Microsoft.AspNetCore.Http;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IFileStorageService
{
    Task<(string StoredFileName, string FullPath)> SaveAsync(Guid workspaceId, Guid datasetId, IFormFile file, CancellationToken ct);
    string ResolvePath(Guid workspaceId, Guid datasetId, string storedFileName, string? persistedPath = null);
    Task DeleteAsync(string fullPath, CancellationToken ct = default);
}
