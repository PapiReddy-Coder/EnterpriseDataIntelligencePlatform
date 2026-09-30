namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IExportStorageService
{
    string CreateStorageKey(Guid exportId, string extension);
    Task SaveAsync(string storageKey, Stream content, CancellationToken ct);
    Task<(Stream Stream, string ContentType)?> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);
}
