using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class LocalFileStorageService(IConfiguration configuration, IWebHostEnvironment environment) : IFileStorageService
{
    public async Task<(string StoredFileName, string FullPath)> SaveAsync(Guid workspaceId, Guid datasetId, IFormFile file, CancellationToken ct)
    {
        var root = GetStorageRoot();

        var directory = Path.Combine(root, workspaceId.ToString("N"), datasetId.ToString("N"));
        Directory.CreateDirectory(directory);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, storedFileName);

        await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await file.CopyToAsync(stream, ct);
        return (storedFileName, fullPath);
    }

    public string ResolvePath(
        Guid workspaceId,
        Guid datasetId,
        string storedFileName,
        string? persistedPath = null)
    {
        // Preserve existing absolute paths while they remain valid.
        if (!string.IsNullOrWhiteSpace(persistedPath) &&
            Path.IsPathRooted(persistedPath) &&
            File.Exists(persistedPath))
        {
            return Path.GetFullPath(persistedPath);
        }

        var root = GetStorageRoot();

        // Support future database records that store a path relative to the
        // configured storage root.
        if (!string.IsNullOrWhiteSpace(persistedPath) &&
            !Path.IsPathRooted(persistedPath))
        {
            var relativeCandidate = Path.GetFullPath(
                Path.Combine(root, persistedPath));

            if (File.Exists(relativeCandidate))
                return relativeCandidate;
        }

        // Recover from a project move when App_Data was moved with the project:
        // reconstruct the canonical path from stable database metadata.
        return Path.Combine(
            root,
            workspaceId.ToString("N"),
            datasetId.ToString("N"),
            Path.GetFileName(storedFileName));
    }

    public Task DeleteAsync(string fullPath, CancellationToken ct = default)
    {
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string GetStorageRoot()
    {
        var configuredRoot = configuration["Ingestion:StorageRoot"];

        if (string.IsNullOrWhiteSpace(configuredRoot))
            return Path.Combine(environment.ContentRootPath, "App_Data", "Imports");

        return Path.IsPathRooted(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
    }
}
