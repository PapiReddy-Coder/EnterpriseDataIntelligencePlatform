using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record ExportFilter(
    [Required, MaxLength(200)] string Column,
    [Required, MaxLength(30)] string Operator,
    [MaxLength(4000)] string? Value);

public sealed record ExportSort(
    [Required, MaxLength(200)] string Column,
    [MaxLength(10)] string Direction = "asc");

public sealed record CreateExportRequest(
    Guid DatasetId,
    Guid? DatasetVersionId,
    [Required, MaxLength(20)] string Format,
    IReadOnlyList<Guid>? ColumnIds,
    IReadOnlyList<ExportFilter>? Filters,
    IReadOnlyList<ExportSort>? Sorts,
    [Range(1, int.MaxValue)] int? Page = null,
    [Range(1, 10000)] int? PageSize = null);

public sealed record ExportRequestResponse(
    Guid Id, Guid DatasetId, Guid? DatasetVersionId, string Format, string Status,
    long? RecordCount, DateTime RequestedAtUtc, DateTime? CompletedAtUtc,
    DateTime ExpiresAtUtc, string? FileName, string? DownloadUrl, string? FailureReason);

public sealed record ExportHistorySearchRequest(
    Guid? DatasetId = null, Guid? UserId = null, Guid? WorkspaceId = null,
    string? Status = null, string? Format = null, DateTime? FromUtc = null,
    DateTime? ToUtc = null, int Page = 1, int PageSize = 20);

public sealed record ExportHistoryResponse(
    Guid Id, Guid DatasetId, string DatasetName, Guid? DatasetVersionId,
    Guid RequestedByUserId, string RequestedByUser, string Format,
    string Status, string? AppliedFilters, DateTime RequestedAtUtc,
    long? RecordCount, DateTime? CompletionTimeUtc, string? FailureReason,
    DateTime ExpiresAtUtc);

public sealed record CreateDatasetShareRequest(
    Guid DatasetId,
    Guid RecipientUserId,
    [Required, MaxLength(20)] string AccessLevel,
    DateTime? ExpiryDateUtc = null);

public sealed record DatasetShareResponse(
    Guid Id, Guid DatasetId, Guid RecipientUserId, string RecipientUser,
    string AccessLevel, Guid SharedByUserId, string SharedByUser,
    DateTime SharedDateUtc, DateTime? ExpiryDateUtc, string Status,
    Guid DatasetAccessGrantId);

public sealed record RevokeDatasetShareRequest([Required, MaxLength(1000)] string? Reason);

public sealed record DatasetShareHistoryResponse(
    Guid Id, Guid DatasetId, Guid DatasetAccessGrantId, Guid RecipientUserId,
    string RecipientUser, Guid SharedByUserId, string SharedByUser,
    string AccessLevel, DateTime SharedDateUtc, DateTime? ExpiryDateUtc,
    string Status, string Action, DateTime? RevokedDateUtc, Guid? RevokedByUserId,
    string? Reason);

public sealed record ShareHistorySearchRequest(
    Guid? DatasetId = null, Guid? RecipientUserId = null, Guid? WorkspaceId = null,
    string? Status = null, DateTime? FromUtc = null, DateTime? ToUtc = null,
    int Page = 1, int PageSize = 20);

public sealed record ExportDashboardResponse(
    long TotalExportRequests, long CompletedExports, long FailedExports,
    long PendingExports, IReadOnlyList<ExportDatasetCount> ExportsByDataset,
    IReadOnlyList<ExportUserCount> ExportsByUser,
    IReadOnlyList<ExportDateCount> ExportActivityByDate, long ActiveDatasetShares,
    long ExpiredDatasetShares, long RevokedDatasetShares);

public sealed record ExportDatasetCount(Guid DatasetId, string DatasetName, long Count);
public sealed record ExportUserCount(Guid UserId, string UserName, long Count);
public sealed record ExportDateCount(DateTime DateUtc, long Count);
