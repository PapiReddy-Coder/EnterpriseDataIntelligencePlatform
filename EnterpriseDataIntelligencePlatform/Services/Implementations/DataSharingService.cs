using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class DataSharingService(
    AppDbContext db,
    ICurrentUser currentUser,
    IDatasetAccessPolicy access,
    IAuditService audit,
    IBackgroundJobQueue queue,
    IExportStorageService storage,
    ILogger<DataSharingService> logger,
    IConfiguration configuration) : IDataSharingService
{
    private const int BatchSize = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ServiceResult<ExportRequestResponse>> CreateExportAsync(CreateExportRequest request, CancellationToken ct)
    {
        if (!DataExportFormats.All.Contains(request.Format, StringComparer.OrdinalIgnoreCase))
            return Fail<ExportRequestResponse>("Export format must be CSV or Excel.", 400);
        if (request.Page.HasValue != request.PageSize.HasValue)
            return Fail<ExportRequestResponse>("Page and PageSize must be supplied together.", 400);
        if (!currentUser.UserId.HasValue || !currentUser.WorkspaceId.HasValue)
            return Fail<ExportRequestResponse>("Authentication and workspace context are required.", 401);

        var dataset = await DatasetQuery().SingleOrDefaultAsync(x => x.Id == request.DatasetId, ct);
        if (dataset is null) return Fail<ExportRequestResponse>("Dataset was not found.", 404);
        if (dataset.Status == DatasetStatuses.Archived || dataset.Governance?.GovernanceStatus == GovernanceStatuses.Archived)
            return Fail<ExportRequestResponse>("Archived datasets cannot be exported.", 403);

        var authorization = await access.AuthorizeAsync(dataset.Id, DatasetAccessLevels.Read, ct);
        if (!authorization.Succeeded) return Fail<ExportRequestResponse>(authorization.Error!, authorization.StatusCode);

        var version = await ResolveVersionAsync(dataset, request.DatasetVersionId, ct);
        if (!version.Succeeded) return Fail<ExportRequestResponse>(version.Error!, version.StatusCode);

        var columns = await ResolveColumnsAsync(dataset.Id, request.ColumnIds, ct);
        if (!columns.Succeeded) return Fail<ExportRequestResponse>(columns.Error!, columns.StatusCode);

        var filters = NormalizeFilters(request.Filters);
        var sorts = NormalizeSorts(request.Sorts);
        var validation = await ValidateQueryShapeAsync(dataset.Id, filters, sorts, columns.Value!, ct);
        if (!validation.Succeeded) return Fail<ExportRequestResponse>(validation.Error!, validation.StatusCode);

        var query = BuildRecordQuery(dataset.Id, filters, sorts);
        var total = await query.LongCountAsync(ct);
        var effectiveCount = total;
        if (request.Page.HasValue && request.PageSize.HasValue)
        {
            effectiveCount = Math.Max(0, Math.Min(request.PageSize.Value, total - ((long)request.Page.Value - 1) * request.PageSize.Value));
        }

        var entity = new DataExportRequest
        {
            WorkspaceId = dataset.WorkspaceId,
            DatasetId = dataset.Id,
            DatasetVersionId = version.Value!.Id,
            RequestedByUserId = currentUser.UserId.Value,
            Format = CanonicalFormat(request.Format),
            SelectedColumnIdsJson = JsonSerializer.Serialize((columns.Value ?? Array.Empty<DatasetColumn>()).Select(x => x.Id).ToArray()),
            FiltersJson = JsonSerializer.Serialize(filters),
            SortsJson = JsonSerializer.Serialize(sorts),
            Page = request.Page,
            PageSize = request.PageSize,
            Status = DataExportStatuses.Pending,
            RecordCount = effectiveCount,
            RequestedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(configuration.GetValue("Exports:RetentionHours", 24))
        };
        db.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("ExportRequested", "DataExportRequest", entity.Id.ToString(),
            $"{entity.Format} export requested for dataset {dataset.Code}.", workspaceId: dataset.WorkspaceId, cancellationToken: ct);

        if (total <= configuration.GetValue<long>("Exports:SynchronousRecordThreshold", 10000))
        {
            await ProcessExportAsync(entity.Id, ct);
            var completed = await GetExportResponseAsync(entity.Id, ct);
            return completed.Succeeded ? ServiceResult<ExportRequestResponse>.Success(completed.Value!, 200) : completed;
        }

        await queue.EnqueueAsync(new BackgroundJob(BackgroundJobTypes.Export, entity.Id), ct);
        return ServiceResult<ExportRequestResponse>.Success(ToResponse(entity), 202);
    }

    public async Task<ServiceResult<ExportRequestResponse>> GetExportAsync(Guid exportId, CancellationToken ct)
    {
        var entity = await db.DataExportRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == exportId, ct);
        if (entity is null) return Fail<ExportRequestResponse>("Export request was not found.", 404);
        var authorized = await CanAccessExportAsync(entity, ct);
        if (!authorized.Succeeded) return Fail<ExportRequestResponse>(authorized.Error!, authorized.StatusCode);
        return ServiceResult<ExportRequestResponse>.Success(ToResponse(entity));
    }

    public async Task<ServiceResult<ExportRequestResponse>> CancelExportAsync(Guid exportId, CancellationToken ct)
    {
        var entity = await db.DataExportRequests.SingleOrDefaultAsync(x => x.Id == exportId, ct);
        if (entity is null) return Fail<ExportRequestResponse>("Export request was not found.", 404);
        var authorized = await CanAccessExportAsync(entity, ct);
        if (!authorized.Succeeded) return Fail<ExportRequestResponse>(authorized.Error!, authorized.StatusCode);
        if (entity.Status is DataExportStatuses.Completed or DataExportStatuses.Failed or DataExportStatuses.Cancelled)
            return Fail<ExportRequestResponse>("The export cannot be cancelled in its current state.", 409);
        entity.Status = DataExportStatuses.Cancelled;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.FailureReason = "Cancelled by user.";
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("ExportCancelled", "DataExportRequest", entity.Id.ToString(), "Export cancelled.", workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ServiceResult<ExportRequestResponse>.Success(ToResponse(entity));
    }

    public async Task<ServiceResult<PagedResponse<ExportHistoryResponse>>> SearchExportsAsync(ExportHistorySearchRequest request, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return Fail<PagedResponse<ExportHistoryResponse>>("Authentication is required.", 401);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var query = db.DataExportRequests.AsNoTracking().Include(x => x.Dataset).Include(x => x.RequestedByUser).AsQueryable();

        if (!currentUser.IsPlatformAdministrator)
            query = query.Where(x => x.WorkspaceId == currentUser.WorkspaceId);
        if (request.DatasetId.HasValue) query = query.Where(x => x.DatasetId == request.DatasetId);
        if (request.UserId.HasValue) query = query.Where(x => x.RequestedByUserId == request.UserId);
        if (request.WorkspaceId.HasValue) query = query.Where(x => x.WorkspaceId == request.WorkspaceId.Value);
        if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Format)) query = query.Where(x => x.Format == CanonicalFormat(request.Format));
        if (request.FromUtc.HasValue) query = query.Where(x => x.RequestedAtUtc >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(x => x.RequestedAtUtc <= request.ToUtc.Value);

        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RequestedAtUtc).Skip((page - 1) * size).Take(size)
            .Select(x => new ExportHistoryResponse(x.Id, x.DatasetId, x.Dataset.Name, x.DatasetVersionId,
                x.RequestedByUserId, x.RequestedByUser.FullName, x.Format, x.Status, x.FiltersJson,
                x.RequestedAtUtc, x.RecordCount, x.CompletedAtUtc, x.FailureReason, x.ExpiresAtUtc)).ToListAsync(ct);
        return ServiceResult<PagedResponse<ExportHistoryResponse>>.Success(new(rows, page, size, checked((int)Math.Min(total, int.MaxValue))));
    }

    public async Task<ServiceResult<ExportDashboardResponse>> DashboardAsync(CancellationToken ct)
    {
        var query = db.DataExportRequests.AsNoTracking();
        if (!currentUser.IsPlatformAdministrator) query = query.Where(x => x.WorkspaceId == currentUser.WorkspaceId);
        var total = await query.LongCountAsync(ct);
        var completed = await query.LongCountAsync(x => x.Status == DataExportStatuses.Completed, ct);
        var failed = await query.LongCountAsync(x => x.Status == DataExportStatuses.Failed, ct);
        var pending = await query.LongCountAsync(x => x.Status == DataExportStatuses.Pending || x.Status == DataExportStatuses.Processing, ct);
        var byDataset = await query.GroupBy(x => new { x.DatasetId, x.Dataset.Name }).Select(g => new ExportDatasetCount(g.Key.DatasetId, g.Key.Name, g.LongCount())).OrderByDescending(x => x.Count).Take(50).ToListAsync(ct);
        var byUser = await query.GroupBy(x => new { x.RequestedByUserId, x.RequestedByUser.FullName }).Select(g => new ExportUserCount(g.Key.RequestedByUserId, g.Key.FullName, g.LongCount())).OrderByDescending(x => x.Count).Take(50).ToListAsync(ct);
        var activity = await query.GroupBy(x => x.RequestedAtUtc.Date).Select(g => new ExportDateCount(g.Key, g.LongCount())).OrderBy(x => x.DateUtc).ToListAsync(ct);
        var shares = db.DatasetAccessGrants.AsNoTracking();
        if (!currentUser.IsPlatformAdministrator) shares = shares.Where(x => x.WorkspaceId == currentUser.WorkspaceId);
        var active = await shares.LongCountAsync(x => x.Status == AccessGrantStatuses.Active && (!x.ExpiryDateUtc.HasValue || x.ExpiryDateUtc > DateTime.UtcNow), ct);
        var expired = await shares.LongCountAsync(x => x.Status == AccessGrantStatuses.Expired || (x.Status == AccessGrantStatuses.Active && x.ExpiryDateUtc.HasValue && x.ExpiryDateUtc <= DateTime.UtcNow), ct);
        var revoked = await shares.LongCountAsync(x => x.Status == AccessGrantStatuses.Revoked, ct);
        return ServiceResult<ExportDashboardResponse>.Success(new(total, completed, failed, pending, byDataset, byUser, activity, active, expired, revoked));
    }

    public async Task<ServiceResult<ExportDownload>> DownloadExportAsync(Guid exportId, CancellationToken ct)
    {
        var entity = await db.DataExportRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == exportId, ct);
        if (entity is null) return Fail<ExportDownload>("Export request was not found.", 404);
        var authorized = await CanAccessExportAsync(entity, ct);
        if (!authorized.Succeeded) return Fail<ExportDownload>(authorized.Error!, authorized.StatusCode);
        if (entity.Status != DataExportStatuses.Completed || string.IsNullOrWhiteSpace(entity.StorageKey))
            return Fail<ExportDownload>("The export is not available for download.", 409);
        if (entity.ExpiresAtUtc <= DateTime.UtcNow)
            return Fail<ExportDownload>("The export has expired.", 410);
        var file = await storage.OpenReadAsync(entity.StorageKey, ct);
        if (file is null) return Fail<ExportDownload>("The export file is no longer available.", 404);
        await audit.WriteAsync("ExportDownloaded", "DataExportRequest", entity.Id.ToString(),
            "Export file downloaded.", workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ServiceResult<ExportDownload>.Success(new(file.Value.Stream, file.Value.ContentType, entity.FileName ?? $"export-{entity.Id:N}"));
    }

    public async Task<ServiceResult<DatasetShareResponse>> ShareDatasetAsync(CreateDatasetShareRequest request, CancellationToken ct) =>
        await UpsertShareAsync(request, null, ct);

    public async Task<ServiceResult<DatasetShareResponse>> UpdateShareAsync(Guid grantId, CreateDatasetShareRequest request, CancellationToken ct) =>
        await UpsertShareAsync(request, grantId, ct);

    public async Task<ServiceResult<DatasetShareResponse>> RevokeShareAsync(Guid grantId, RevokeDatasetShareRequest request, CancellationToken ct)
    {
        var grant = await db.DatasetAccessGrants.Include(x => x.Dataset).Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Id == grantId, ct);
        if (grant is null) return Fail<DatasetShareResponse>("Dataset access grant was not found.", 404);
        var manageLevel = await EffectiveAccessLevelAsync(grant.DatasetId, ct);
        if (DatasetAccessLevels.Rank(manageLevel) < DatasetAccessLevels.Rank(DatasetAccessLevels.Manage))
            return Fail<DatasetShareResponse>("Only a dataset owner, steward, administrator, or Manage-level grantee can revoke a share.", 403);
        if (grant.Status != AccessGrantStatuses.Active) return Fail<DatasetShareResponse>("The dataset share is not active.", 409);

        var now = DateTime.UtcNow;
        grant.Status = AccessGrantStatuses.Revoked;
        grant.RevokedByUserId = currentUser.UserId;
        grant.RevokedDateUtc = now;
        var source = await db.DatasetAccessRequests.SingleOrDefaultAsync(x => x.Id == grant.SourceAccessRequestId, ct);
        if (source is not null) source.Status = AccessRequestStatuses.Revoked;

        var history = new DatasetShareHistory
        {
            WorkspaceId = grant.WorkspaceId, DatasetId = grant.DatasetId, DatasetAccessGrantId = grant.Id,
            RecipientUserId = grant.UserId, SharedByUserId = currentUser.UserId!.Value,
            AccessLevel = grant.AccessLevel, SharedDateUtc = grant.GrantedDateUtc, ExpiryDateUtc = grant.ExpiryDateUtc,
            Status = DatasetShareStatuses.Revoked, Action = DatasetShareActions.Revoked,
            RevokedDateUtc = now, RevokedByUserId = currentUser.UserId, Reason = request.Reason?.Trim()
        };
        db.DatasetShareHistories.Add(history);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetShareRevoked", "DatasetAccessGrant", grant.Id.ToString(), request.Reason?.Trim(), workspaceId: grant.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetShareResponse>.Success(await ToShareResponseAsync(grant, ct));
    }

    public async Task<ServiceResult<PagedResponse<DatasetShareHistoryResponse>>> SearchSharesAsync(ShareHistorySearchRequest request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var query = db.DatasetShareHistories.AsNoTracking().Include(x => x.Dataset)
            .Include(x => x.RecipientUser).Include(x => x.SharedByUser).AsQueryable();
        if (!currentUser.IsPlatformAdministrator) query = query.Where(x => x.WorkspaceId == currentUser.WorkspaceId);
        if (request.DatasetId.HasValue) query = query.Where(x => x.DatasetId == request.DatasetId);
        if (request.RecipientUserId.HasValue) query = query.Where(x => x.RecipientUserId == request.RecipientUserId);
        if (request.WorkspaceId.HasValue) query = query.Where(x => x.WorkspaceId == request.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status);
        if (request.FromUtc.HasValue) query = query.Where(x => x.SharedDateUtc >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(x => x.SharedDateUtc <= request.ToUtc.Value);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.SharedDateUtc).Skip((page - 1) * size).Take(size)
            .Select(x => new DatasetShareHistoryResponse(x.Id, x.DatasetId, x.DatasetAccessGrantId,
                x.RecipientUserId, x.RecipientUser.FullName, x.SharedByUserId, x.SharedByUser.FullName,
                x.AccessLevel, x.SharedDateUtc, x.ExpiryDateUtc, x.Status, x.Action,
                x.RevokedDateUtc, x.RevokedByUserId, x.Reason)).ToListAsync(ct);
        return ServiceResult<PagedResponse<DatasetShareHistoryResponse>>.Success(new(rows, page, size, checked((int)Math.Min(total, int.MaxValue))));
    }

    public async Task ProcessExportAsync(Guid exportId, CancellationToken ct)
    {
        var entity = await db.DataExportRequests.SingleOrDefaultAsync(x => x.Id == exportId, ct);
        if (entity is null || entity.Status is DataExportStatuses.Completed or DataExportStatuses.Cancelled) return;
        try
        {
            var authorization = await CanAccessExportAsync(entity, ct);
            if (!authorization.Succeeded) throw new ExportProcessingException(authorization.Error ?? "Export authorization failed.", authorization.StatusCode);
            var dataset = await DatasetQuery().SingleOrDefaultAsync(x => x.Id == entity.DatasetId, ct);
            if (dataset is null || dataset.Status == DatasetStatuses.Archived || dataset.Governance?.GovernanceStatus == GovernanceStatuses.Archived)
                throw new ExportProcessingException("Dataset is no longer exportable.", 403);

            entity.Status = DataExportStatuses.Processing;
            entity.ProcessingStartedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var columns = await ResolveColumnsByIdsAsync(entity.DatasetId, JsonSerializer.Deserialize<Guid[]>(entity.SelectedColumnIdsJson) ?? [], ct);
            if (columns.Count == 0) throw new InvalidOperationException("No export columns are available.");
            var filters = JsonSerializer.Deserialize<List<ExportFilter>>(entity.FiltersJson, JsonOptions) ?? [];
            var sorts = JsonSerializer.Deserialize<List<ExportSort>>(entity.SortsJson, JsonOptions) ?? [];
            var query = BuildRecordQuery(entity.DatasetId, filters, sorts);
            if (entity.Page.HasValue && entity.PageSize.HasValue)
                query = query.Skip((entity.Page.Value - 1) * entity.PageSize.Value).Take(entity.PageSize.Value);

            var extension = entity.Format == DataExportFormats.Excel ? "xlsx" : "csv";
            var storageKey = storage.CreateStorageKey(entity.Id, extension);
            var temp = Path.Combine(Path.GetTempPath(), $"edip-export-{entity.Id:N}.{extension}");
            try
            {
                await GenerateFileAsync(temp, entity.Format, columns, query, ct);
                await using var generated = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                await storage.SaveAsync(storageKey, generated, ct);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }

            entity.StorageKey = storageKey;
            entity.FileName = $"{dataset.Code}-{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}";
            entity.Status = DataExportStatuses.Completed;
            entity.CompletedAtUtc = DateTime.UtcNow;
            entity.ExpiresAtUtc = DateTime.UtcNow.AddHours(configuration.GetValue("Exports:RetentionHours", 24));
            entity.FailureReason = null;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("ExportCompleted", "DataExportRequest", entity.Id.ToString(),
                $"{entity.Format} export completed with {entity.RecordCount ?? 0} records.", workspaceId: entity.WorkspaceId, cancellationToken: ct);
        }
        catch (ExportProcessingException ex)
        {
            await MarkExportFailedAsync(entity, ex.Message, ct);
            logger.LogWarning("Export {ExportId} denied during processing: {Reason}", exportId, ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await MarkExportFailedAsync(entity, "Export processing failed.", ct);
            logger.LogError(ex, "Export processing failed for {ExportId}.", exportId);
        }
    }

    public async Task CleanupExpiredExportsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await db.DataExportRequests.Where(x => x.ExpiresAtUtc <= now && x.StorageKey != null)
            .Select(x => new { x.Id, x.StorageKey }).Take(100).ToListAsync(ct);
        foreach (var item in expired)
        {
            try { await storage.DeleteAsync(item.StorageKey!, ct); } catch (Exception ex) { logger.LogWarning(ex, "Could not delete expired export {ExportId}.", item.Id); }
            var entity = await db.DataExportRequests.SingleOrDefaultAsync(x => x.Id == item.Id, ct);
            if (entity is not null) entity.StorageKey = null;
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task<ServiceResult<DatasetShareResponse>> UpsertShareAsync(CreateDatasetShareRequest request, Guid? grantId, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue || !currentUser.WorkspaceId.HasValue) return Fail<DatasetShareResponse>("Authentication and workspace context are required.", 401);
        if (!DatasetAccessLevels.All.Contains(request.AccessLevel, StringComparer.OrdinalIgnoreCase)) return Fail<DatasetShareResponse>("Invalid access level.", 400);
        if (request.ExpiryDateUtc.HasValue && request.ExpiryDateUtc <= DateTime.UtcNow) return Fail<DatasetShareResponse>("Expiry date must be in the future.", 400);

        var dataset = await DatasetQuery().SingleOrDefaultAsync(x => x.Id == request.DatasetId, ct);
        if (dataset is null) return Fail<DatasetShareResponse>("Dataset was not found.", 404);
        if (dataset.Status == DatasetStatuses.Archived || dataset.Governance?.GovernanceStatus == GovernanceStatuses.Archived)
            return Fail<DatasetShareResponse>("Archived datasets cannot be shared.", 403);
        var manageLevel = await EffectiveAccessLevelAsync(dataset.Id, ct);
        if (DatasetAccessLevels.Rank(manageLevel) < DatasetAccessLevels.Rank(DatasetAccessLevels.Manage))
            return Fail<DatasetShareResponse>("Only a dataset owner, steward, administrator, or Manage-level grantee can share a dataset.", 403);

        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RecipientUserId && x.IsActive, ct);
        if (recipient is null || recipient.WorkspaceId != dataset.WorkspaceId)
            return Fail<DatasetShareResponse>("The recipient must be an active user in the same workspace.", 400);
        if (recipient.Id == currentUser.UserId) return Fail<DatasetShareResponse>("A dataset cannot be shared with the current user.", 400);

        DatasetAccessGrant? grant;
        if (grantId.HasValue)
        {
            grant = await db.DatasetAccessGrants.Include(x => x.User).Include(x => x.Dataset)
                .SingleOrDefaultAsync(x => x.Id == grantId.Value && x.DatasetId == dataset.Id, ct);
            if (grant is null) return Fail<DatasetShareResponse>("Dataset access grant was not found.", 404);
            if (grant.UserId != recipient.Id) return Fail<DatasetShareResponse>("The recipient cannot be changed for an existing share.", 400);
            if (DatasetAccessLevels.Rank(request.AccessLevel) > DatasetAccessLevels.Rank(await EffectiveAccessLevelAsync(dataset.Id, ct)))
                return Fail<DatasetShareResponse>("You cannot grant an access level higher than your own effective access level.", 403);
            if (grant.Status != AccessGrantStatuses.Active) return Fail<DatasetShareResponse>("Only an active share can be updated.", 409);
            grant.AccessLevel = request.AccessLevel;
            grant.ExpiryDateUtc = request.ExpiryDateUtc;
            grant.GrantedByUserId = currentUser.UserId.Value;
            grant.GrantedDateUtc = DateTime.UtcNow;
        }
        else
        {
            var ownLevel = await EffectiveAccessLevelAsync(dataset.Id, ct);
            if (DatasetAccessLevels.Rank(request.AccessLevel) > DatasetAccessLevels.Rank(ownLevel))
                return Fail<DatasetShareResponse>("You cannot grant an access level higher than your own permitted level.", 403);
            grant = await db.DatasetAccessGrants.Include(x => x.User).Include(x => x.Dataset)
                .SingleOrDefaultAsync(x => x.DatasetId == dataset.Id && x.UserId == recipient.Id && x.Status == AccessGrantStatuses.Active, ct);
            if (grant is not null)
            {
                grant.AccessLevel = request.AccessLevel;
                grant.ExpiryDateUtc = request.ExpiryDateUtc;
                grant.GrantedByUserId = currentUser.UserId.Value;
                grant.GrantedDateUtc = DateTime.UtcNow;
            }
            else
            {
                var accessRequest = new DatasetAccessRequest
                {
                    WorkspaceId = dataset.WorkspaceId,
                    DatasetId = dataset.Id,
                    RequestingUserId = recipient.Id,
                    RequestedAccessLevel = request.AccessLevel,
                    BusinessJustification = $"Dataset shared by user {currentUser.UserId.Value}.",
                    RequestDateUtc = DateTime.UtcNow,
                    Status = AccessRequestStatuses.Approved,
                    ReviewerId = currentUser.UserId.Value,
                    ReviewComments = "Approved through controlled dataset sharing.",
                    ReviewedDateUtc = DateTime.UtcNow,
                    RequestedExpiryDateUtc = request.ExpiryDateUtc
                };
                db.DatasetAccessRequests.Add(accessRequest);
                grant = new DatasetAccessGrant
                {
                    WorkspaceId = dataset.WorkspaceId, DatasetId = dataset.Id, UserId = recipient.Id,
                    AccessLevel = request.AccessLevel, GrantedByUserId = currentUser.UserId.Value,
                    GrantedDateUtc = DateTime.UtcNow, ExpiryDateUtc = request.ExpiryDateUtc,
                    Status = AccessGrantStatuses.Active, SourceAccessRequest = accessRequest
                };
                db.DatasetAccessGrants.Add(grant);
            }
        }

        await db.SaveChangesAsync(ct);
        var history = new DatasetShareHistory
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = dataset.Id, DatasetAccessGrantId = grant.Id,
            RecipientUserId = grant.UserId, SharedByUserId = currentUser.UserId.Value, AccessLevel = grant.AccessLevel,
            SharedDateUtc = grant.GrantedDateUtc, ExpiryDateUtc = grant.ExpiryDateUtc,
            Status = grant.ExpiryDateUtc.HasValue && grant.ExpiryDateUtc <= DateTime.UtcNow ? DatasetShareStatuses.Expired : DatasetShareStatuses.Active,
            Action = grantId.HasValue ? DatasetShareActions.Updated : DatasetShareActions.Shared
        };
        db.DatasetShareHistories.Add(history);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(grantId.HasValue ? "DatasetShareUpdated" : "DatasetShared", "DatasetAccessGrant", grant.Id.ToString(),
            $"Dataset {dataset.Code} shared with {recipient.FullName} at {grant.AccessLevel}.", workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetShareResponse>.Success(await ToShareResponseAsync(grant, ct), grantId.HasValue ? 200 : 201);
    }

    private async Task<ServiceResult<bool>> CanAccessExportAsync(DataExportRequest entity, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue || !currentUser.WorkspaceId.HasValue)
            return ServiceResult<bool>.Failure("Authentication and workspace context are required.", 401);
        if (entity.WorkspaceId != currentUser.WorkspaceId && !currentUser.IsPlatformAdministrator)
            return ServiceResult<bool>.Failure("Export was not found.", 404);
        if (entity.ExpiresAtUtc <= DateTime.UtcNow) return ServiceResult<bool>.Failure("The export has expired.", 410);
        var datasetAccess = await access.AuthorizeAsync(entity.DatasetId, DatasetAccessLevels.Read, ct);
        if (!datasetAccess.Succeeded) return datasetAccess;
        return ServiceResult<bool>.Success(true);
    }

    private IQueryable<Dataset> DatasetQuery() => db.Datasets.Include(x => x.Governance).AsNoTracking();

    private async Task<ServiceResult<DatasetVersion>> ResolveVersionAsync(Dataset dataset, Guid? versionId, CancellationToken ct)
    {
        var query = db.DatasetVersions.AsNoTracking().Where(x => x.DatasetId == dataset.Id);
        DatasetVersion? version;
        if (versionId.HasValue) version = await query.SingleOrDefaultAsync(x => x.Id == versionId.Value, ct);
        else version = await query.SingleOrDefaultAsync(x => x.IsCurrent || x.VersionNumber == dataset.CurrentVersion, ct);
        return version is null ? Fail<DatasetVersion>("The requested dataset version was not found.", 404) : ServiceResult<DatasetVersion>.Success(version);
    }

    private async Task<ServiceResult<IReadOnlyList<DatasetColumn>>> ResolveColumnsAsync(Guid datasetId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        var columns = await db.DatasetColumns.AsNoTracking().Where(x => x.DatasetId == datasetId).OrderBy(x => x.Ordinal).ToListAsync(ct);
        if (ids is null || ids.Count == 0) return ServiceResult<IReadOnlyList<DatasetColumn>>.Success(columns);
        var selected = ids.Distinct().ToHashSet();
        if (selected.Count != ids.Count || columns.Count(x => selected.Contains(x.Id)) != selected.Count)
            return Fail<IReadOnlyList<DatasetColumn>>("One or more selected columns do not belong to the dataset.", 400);
        return ServiceResult<IReadOnlyList<DatasetColumn>>.Success(columns.Where(x => selected.Contains(x.Id)).OrderBy(x => x.Ordinal).ToList());
    }

    private async Task<List<DatasetColumn>> ResolveColumnsByIdsAsync(Guid datasetId, Guid[] ids, CancellationToken ct) =>
        await db.DatasetColumns.AsNoTracking().Where(x => x.DatasetId == datasetId && ids.Contains(x.Id)).OrderBy(x => x.Ordinal).ToListAsync(ct);

    private static List<ExportFilter> NormalizeFilters(IReadOnlyList<ExportFilter>? filters) =>
        (filters ?? []).Select(x => new ExportFilter(x.Column.Trim(), x.Operator.Trim().ToLowerInvariant(), x.Value)).ToList();

    private static List<ExportSort> NormalizeSorts(IReadOnlyList<ExportSort>? sorts) =>
        (sorts ?? []).Select(x => new ExportSort(x.Column.Trim(), x.Direction.Trim().ToLowerInvariant() == "desc" ? "desc" : "asc")).ToList();

    private async Task<ServiceResult<bool>> ValidateQueryShapeAsync(Guid datasetId, List<ExportFilter> filters, List<ExportSort> sorts, IReadOnlyList<DatasetColumn> columns, CancellationToken ct)
    {
        var allowed = await db.DatasetColumns.AsNoTracking().Where(x => x.DatasetId == datasetId).Select(x => x.Name).ToListAsync(ct);
        var map = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in filters) if (!map.Contains(item.Column)) return Fail<bool>($"Unknown filter column '{item.Column}'.", 400);
        foreach (var item in sorts) if (!map.Contains(item.Column)) return Fail<bool>($"Unknown sort column '{item.Column}'.", 400);
        foreach (var item in filters) if (!new[] { "eq", "neq", "contains", "startswith", "endswith", "gt", "gte", "lt", "lte", "isnull", "notnull" }.Contains(item.Operator)) return Fail<bool>($"Unsupported filter operator '{item.Operator}'.", 400);
        return ServiceResult<bool>.Success(true);
    }

    private IQueryable<DatasetRecord> BuildRecordQuery(Guid datasetId, List<ExportFilter> filters, List<ExportSort> sorts)
    {
        IQueryable<DatasetRecord> query = db.DatasetRecords.AsNoTracking().Where(x => x.DatasetId == datasetId);
        foreach (var filter in filters)
        {
            var column = filter.Column;
            var value = filter.Value ?? string.Empty;
            var op = filter.Operator;
            query = op switch
            {
                "eq" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && (v.StringValue ?? v.RawValue) == value)),
                "neq" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && (v.StringValue ?? v.RawValue) != value)),
                "contains" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && EF.Functions.Like(v.StringValue ?? v.RawValue, $"%{value}%"))),
                "startswith" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && EF.Functions.Like(v.StringValue ?? v.RawValue, $"{value}%"))),
                "endswith" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && EF.Functions.Like(v.StringValue ?? v.RawValue, $"%{value}"))),
                "gt" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && string.Compare(v.StringValue ?? v.RawValue, value) > 0)),
                "gte" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && string.Compare(v.StringValue ?? v.RawValue, value) >= 0)),
                "lt" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && string.Compare(v.StringValue ?? v.RawValue, value) < 0)),
                "lte" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && string.Compare(v.StringValue ?? v.RawValue, value) <= 0)),
                "isnull" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && (v.StringValue == null && v.RawValue == null))),
                "notnull" => query.Where(r => r.Values.Any(v => v.DatasetColumn.Name == column && (v.StringValue != null || v.RawValue != null))),
                _ => query
            };
        }

        if (sorts.Count == 0) return query.OrderBy(x => x.Id);
        IOrderedQueryable<DatasetRecord>? ordered = null;
        foreach (var sort in sorts)
        {
            var column = sort.Column;
            if (ordered is null)
            {
                ordered = sort.Direction == "desc"
                    ? query.OrderByDescending(x => x.Values.Where(v => v.DatasetColumn.Name == column).Select(v => v.StringValue ?? v.RawValue).FirstOrDefault())
                    : query.OrderBy(x => x.Values.Where(v => v.DatasetColumn.Name == column).Select(v => v.StringValue ?? v.RawValue).FirstOrDefault());
            }
            else
            {
                ordered = sort.Direction == "desc"
                    ? ordered.ThenByDescending(x => x.Values.Where(v => v.DatasetColumn.Name == column).Select(v => v.StringValue ?? v.RawValue).FirstOrDefault())
                    : ordered.ThenBy(x => x.Values.Where(v => v.DatasetColumn.Name == column).Select(v => v.StringValue ?? v.RawValue).FirstOrDefault());
            }
        }
        return ordered!;
    }

    private async Task<string> EffectiveAccessLevelAsync(Guid datasetId, CancellationToken ct)
    {
        if (currentUser.IsPlatformAdministrator || await access.IsWorkspaceAdministratorAsync(ct)) return DatasetAccessLevels.Manage;
        var dataset = await db.Datasets.IgnoreQueryFilters().AsNoTracking().Include(x => x.Governance).SingleAsync(x => x.Id == datasetId, ct);
        if (dataset.OwnerId == currentUser.UserId || dataset.Governance?.DataStewardId == currentUser.UserId) return DatasetAccessLevels.Manage;
        var grants = await db.DatasetAccessGrants.AsNoTracking().Where(x => x.DatasetId == datasetId && x.UserId == currentUser.UserId && x.Status == AccessGrantStatuses.Active && (!x.ExpiryDateUtc.HasValue || x.ExpiryDateUtc > DateTime.UtcNow)).Select(x => x.AccessLevel).ToListAsync(ct);
        var rank = grants.Select(DatasetAccessLevels.Rank).DefaultIfEmpty(1).Max();
        return rank switch { 3 => DatasetAccessLevels.Manage, 2 => DatasetAccessLevels.Write, _ => DatasetAccessLevels.Read };
    }

    private async Task<DatasetShareResponse> ToShareResponseAsync(DatasetAccessGrant grant, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == grant.UserId, ct);
        var sharer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == grant.GrantedByUserId, ct);
        return new(grant.Id, grant.DatasetId, grant.UserId, user.FullName, grant.AccessLevel, grant.GrantedByUserId,
            sharer.FullName, grant.GrantedDateUtc, grant.ExpiryDateUtc,
            grant.Status == AccessGrantStatuses.Active && grant.ExpiryDateUtc.HasValue && grant.ExpiryDateUtc <= DateTime.UtcNow ? AccessGrantStatuses.Expired : grant.Status,
            grant.Id);
    }

    private async Task<ServiceResult<ExportRequestResponse>> GetExportResponseAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.DataExportRequests.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        return ServiceResult<ExportRequestResponse>.Success(ToResponse(entity));
    }

    private static ExportRequestResponse ToResponse(DataExportRequest x) => new(x.Id, x.DatasetId, x.DatasetVersionId, x.Format, x.Status, x.RecordCount,
        x.RequestedAtUtc, x.CompletedAtUtc, x.ExpiresAtUtc, x.FileName,
        x.Status == DataExportStatuses.Completed ? $"/api/data-sharing/exports/{x.Id}/download" : null, x.FailureReason);

    private async Task MarkExportFailedAsync(DataExportRequest entity, string reason, CancellationToken ct)
    {
        entity.Status = DataExportStatuses.Failed;
        entity.FailureReason = reason;
        entity.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("ExportFailed", "DataExportRequest", entity.Id.ToString(), reason, workspaceId: entity.WorkspaceId, cancellationToken: ct);
    }

    private async Task GenerateFileAsync(string path, string format, IReadOnlyList<DatasetColumn> columns, IQueryable<DatasetRecord> baseQuery, CancellationToken ct)
    {
        if (format == DataExportFormats.Excel)
            await GenerateExcelAsync(path, columns, baseQuery, ct);
        else
            await GenerateCsvAsync(path, columns, baseQuery, ct);
    }

    private async Task GenerateCsvAsync(string path, IReadOnlyList<DatasetColumn> columns, IQueryable<DatasetRecord> baseQuery, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true), 64 * 1024);
        await writer.WriteLineAsync(string.Join(",", columns.Select(x => Csv(x.Name))).AsMemory(), ct);
        await WriteRowsAsync(columns, baseQuery, async values =>
        {
            await writer.WriteLineAsync(string.Join(",", columns.Select(c => Csv(values.TryGetValue(c.Id, out var v) ? v : null))).AsMemory(), ct);
        }, ct);
    }

    private async Task GenerateExcelAsync(string path, IReadOnlyList<DatasetColumn> columns, IQueryable<DatasetRecord> baseQuery, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024, useAsync: true);
        using var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var writer = OpenXmlWriter.Create(worksheetPart))
        {
            writer.WriteStartElement(new Worksheet());
            writer.WriteStartElement(new SheetData());
            writer.WriteStartElement(new Row());
            foreach (var column in columns) WriteCell(writer, column.Name);
            writer.WriteEndElement();
            await WriteRowsAsync(columns, baseQuery, values =>
            {
                writer.WriteStartElement(new Row());
                foreach (var column in columns) WriteCell(writer, values.TryGetValue(column.Id, out var v) ? v : null);
                writer.WriteEndElement();
                return Task.CompletedTask;
            }, ct);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Name = "Data", SheetId = 1, Id = workbookPart.GetIdOfPart(worksheetPart) });
        workbookPart.Workbook.Save();
    }

    private async Task WriteRowsAsync(IReadOnlyList<DatasetColumn> columns, IQueryable<DatasetRecord> baseQuery, Func<Dictionary<Guid, string?>, Task> writeRow, CancellationToken ct)
    {
        var offset = 0;
        while (true)
        {
            var records = await baseQuery.Skip(offset).Take(BatchSize)
                .Select(x => new { x.Id }).ToListAsync(ct);
            if (records.Count == 0) break;
            var ids = records.Select(x => x.Id).ToArray();
            var values = await db.DatasetRecordValues.AsNoTracking().Where(x => ids.Contains(x.DatasetRecordId) && columns.Select(c => c.Id).Contains(x.DatasetColumnId))
                .Select(x => new { x.DatasetRecordId, x.DatasetColumnId, x.StringValue, x.RawValue, x.DecimalValue, x.IntegerValue, x.BooleanValue, x.DateTimeValue }).ToListAsync(ct);
            var byRecord = values.GroupBy(x => x.DatasetRecordId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.DatasetColumnId, x =>
                x.StringValue ?? x.RawValue ?? (x.DecimalValue.HasValue ? x.DecimalValue.Value.ToString(CultureInfo.InvariantCulture) : null) ??
                (x.IntegerValue.HasValue ? x.IntegerValue.Value.ToString(CultureInfo.InvariantCulture) : null) ??
                (x.BooleanValue.HasValue ? x.BooleanValue.Value.ToString() : null) ??
                (x.DateTimeValue.HasValue ? x.DateTimeValue.Value.ToString("O") : null)));
            foreach (var record in records) await writeRow(byRecord.TryGetValue(record.Id, out var map) ? map : new Dictionary<Guid, string?>());
            offset += records.Count;
            if (records.Count < BatchSize) break;
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static void WriteCell(OpenXmlWriter writer, string? value)
    {
        writer.WriteElement(new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? string.Empty)) });
    }

    private static string CanonicalFormat(string format) => format.Equals(DataExportFormats.Excel, StringComparison.OrdinalIgnoreCase) ? DataExportFormats.Excel : DataExportFormats.Csv;
    private static ServiceResult<T> Fail<T>(string message, int code) => ServiceResult<T>.Failure(message, code);
    private sealed class ExportProcessingException(string message, int statusCode) : Exception(message) { public int StatusCode { get; } = statusCode; }
}
