using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record RegisterDatasetRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description,
    Guid CategoryId,
    Guid OwnerId,
    [Required, MaxLength(200)] string DataSourceName,
    [Required, MaxLength(100)] string DataSourceType,
    [MaxLength(1000)] string? DataSourceDescription,
    IReadOnlyCollection<string>? Tags,
    [MaxLength(1000)] string? VersionNotes,
    Guid? WorkspaceId = null);

public sealed record UpdateDatasetRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description,
    Guid CategoryId,
    Guid OwnerId,
    [Required, MaxLength(200)] string DataSourceName,
    [Required, MaxLength(100)] string DataSourceType,
    [MaxLength(1000)] string? DataSourceDescription,
    IReadOnlyCollection<string>? Tags,
    [MaxLength(1000)] string? VersionNotes);

public sealed record DatasetLifecycleRequest([MaxLength(1000)] string? VersionNotes)
{
    public string? Notes => VersionNotes;
}

public sealed record DatasetSearchRequest(
    string? Keyword = null,
    string? Name = null,
    Guid? CategoryId = null,
    Guid? WorkspaceId = null,
    Guid? OwnerId = null,
    string? Status = null,
    string? Tag = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "updatedAtUtc",
    string SortDirection = "desc");

public sealed record DatasetCatalogItemResponse(
    Guid Id,
    string Code,
    Guid WorkspaceId,
    string Name,
    string Description,
    Guid CategoryId,
    string Category,
    Guid OwnerId,
    string Owner,
    string DataSourceName,
    string DataSourceType,
    string Status,
    int CurrentVersion,
    IReadOnlyList<string> Tags,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record DatasetDetailsResponse(
    Guid Id,
    string Code,
    Guid WorkspaceId,
    string Name,
    string Description,
    Guid CategoryId,
    string Category,
    Guid OwnerId,
    string Owner,
    string DataSourceName,
    string DataSourceType,
    string DataSourceDescription,
    string Status,
    int CurrentVersion,
    bool IsDeleted,
    IReadOnlyList<string> Tags,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DeletedAtUtc);

public sealed record DatasetVersionResponse(
    Guid Id,
    Guid DatasetId,
    string Code,
    int VersionNumber,
    bool IsCurrent,
    string Name,
    string Description,
    Guid CategoryId,
    string Category,
    Guid OwnerId,
    string Owner,
    string DataSourceName,
    string DataSourceType,
    string DataSourceDescription,
    string Status,
    IReadOnlyList<string> Tags,
    string? VersionNotes,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc);

public sealed record DatasetVersionHistoryResponse(
    Guid DatasetId,
    string Code,
    string DatasetName,
    int CurrentVersion,
    int TotalVersions,
    IReadOnlyList<DatasetVersionResponse> Versions);

public sealed record DatasetCategoryResponse(Guid Id, string Name, string Description, bool IsActive);
public sealed record CreateDatasetCategoryRequest([Required, MaxLength(100)] string Name, [MaxLength(500)] string? Description);
public sealed record UpdateDatasetCategoryRequest([Required, MaxLength(100)] string Name, [MaxLength(500)] string? Description, bool IsActive);
