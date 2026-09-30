using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class LocalExportStorageService(IConfiguration configuration, IWebHostEnvironment environment) : IExportStorageService
{
    private readonly string root = GetRoot(configuration, environment);

    public string CreateStorageKey(Guid exportId, string extension) => $"{exportId:N}.{extension.TrimStart('.')}";

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var path = Resolve(storageKey);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await content.CopyToAsync(target, ct);
    }

    public async Task<(Stream Stream, string ContentType)?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (!File.Exists(path)) return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var contentType = extension == ".xlsx"
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv";
        return (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true), contentType);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Resolve(storageKey)));
    }

    private static string GetRoot(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Exports:StorageRoot"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "Exports")
            : Path.IsPathRooted(configured) ? Path.GetFullPath(configured) : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(storageKey) || storageKey.Contains(Path.DirectorySeparatorChar) || storageKey.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException("Invalid export storage key.");
        Directory.CreateDirectory(root);
        return Path.Combine(root, storageKey);
    }
}
