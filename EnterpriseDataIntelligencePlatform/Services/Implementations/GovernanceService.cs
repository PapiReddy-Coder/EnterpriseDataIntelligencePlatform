using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class GovernanceService(
    AppDbContext db, ICurrentUser currentUser, IDatasetAccessPolicy access, IAuditService audit,
    IDatasetService datasets) : IGovernanceService
{
    public async Task<ServiceResult<DatasetGovernanceResponse>> UpsertAsync(Guid datasetId, UpsertDatasetGovernanceRequest request, CancellationToken ct)
    {
        var authorization = await access.AuthorizeAsync(datasetId, DatasetAccessLevels.Manage, ct);
        if (!authorization.Succeeded) return Fail<DatasetGovernanceResponse>(authorization);
        var validation = await ValidateGovernanceAsync(datasetId, request.DataStewardId, request.Classification,
            request.GovernanceStatus, request.LastReviewedDateUtc, request.NextReviewDateUtc, ct);
        if (validation is not null) return ServiceResult<DatasetGovernanceResponse>.Failure(validation, 400);

        var dataset = await GovernanceDatasetQuery().SingleAsync(x => x.Id == datasetId, ct);
        var governance = dataset.Governance;
        var previous = governance is null ? GovernanceStatuses.Draft : EffectiveGovernanceStatus(governance);
        var target = Canonical(request.GovernanceStatus, GovernanceStatuses.All)!;
        if (!CanTransition(previous, target, allowCertification: false))
            return ServiceResult<DatasetGovernanceResponse>.Failure($"Invalid governance transition from '{previous}' to '{target}'.", 409);

        var now = DateTime.UtcNow;
        if (governance is null)
        {
            governance = new DatasetGovernance
            {
                WorkspaceId = dataset.WorkspaceId, DatasetId = dataset.Id,
                UpdatedByUserId = currentUser.UserId!.Value, CreatedAtUtc = now
            };
            db.DatasetGovernance.Add(governance);
            dataset.Governance = governance;
        }

        var oldClassification = governance.Classification;
        var oldSteward = governance.DataStewardId;
        governance.DataStewardId = request.DataStewardId;
        governance.BusinessDescription = request.BusinessDescription.Trim();
        governance.GovernanceStatus = target;
        governance.Classification = Canonical(request.Classification, DataClassifications.All)!;
        governance.LastReviewedDateUtc = request.LastReviewedDateUtc;
        governance.NextReviewDateUtc = request.NextReviewDateUtc;
        governance.UpdatedByUserId = currentUser.UserId!.Value;
        governance.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync("DatasetGovernanceUpdated", "DatasetGovernance", governance.Id.ToString(),
            $"Classification: {oldClassification} -> {governance.Classification}; Steward: {oldSteward} -> {governance.DataStewardId}; Governance status: {previous} -> {target}.",
            workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetGovernanceResponse>.Success(ToGovernance(dataset));
    }

    public async Task<ServiceResult<DatasetGovernanceResponse>> GetAsync(Guid datasetId, CancellationToken ct)
    {
        var authorization = await access.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!authorization.Succeeded) return Fail<DatasetGovernanceResponse>(authorization);
        var dataset = await GovernanceDatasetQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId, ct);
        return dataset is null
            ? ServiceResult<DatasetGovernanceResponse>.Failure("Dataset was not found.", 404)
            : ServiceResult<DatasetGovernanceResponse>.Success(ToGovernance(dataset));
    }

    public async Task<ServiceResult<DatasetGovernanceResponse>> UpdateClassificationAsync(Guid datasetId, UpdateClassificationRequest request, CancellationToken ct)
    {
        var current = await GovernanceDatasetQuery().AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId, ct);
        if (current is null) return ServiceResult<DatasetGovernanceResponse>.Failure("Dataset was not found.", 404);
        var g = current.Governance;
        return await UpsertAsync(datasetId, new UpsertDatasetGovernanceRequest(g?.DataStewardId,
            g?.BusinessDescription ?? current.Description, EffectiveGovernanceStatus(g), request.Classification,
            g?.LastReviewedDateUtc, g?.NextReviewDateUtc), ct);
    }

    public async Task<ServiceResult<DatasetGovernanceResponse>> AssignUsersAsync(Guid datasetId, AssignGovernanceUsersRequest request, CancellationToken ct)
    {
        if (request.DataOwnerId == Guid.Empty)
            return ServiceResult<DatasetGovernanceResponse>.Failure("DataOwnerId is required.", 400);
        var current = await GovernanceDatasetQuery().AsNoTracking()
            .Include(x => x.Category).Include(x => x.DatasetTags).ThenInclude(x => x.Tag)
            .SingleOrDefaultAsync(x => x.Id == datasetId, ct);
        if (current is null) return ServiceResult<DatasetGovernanceResponse>.Failure("Dataset was not found.", 404);
        if (current.OwnerId != request.DataOwnerId)
        {
            var update = await datasets.UpdateAsync(datasetId, new UpdateDatasetRequest(current.Name, current.Description,
                current.CategoryId, request.DataOwnerId, current.DataSourceName, current.DataSourceType,
                current.DataSourceDescription, current.DatasetTags.Select(x => x.Tag.Name).ToList(),
                "Data owner reassigned through governance."), ct);
            if (!update.Succeeded) return ServiceResult<DatasetGovernanceResponse>.Failure(update.Error!, update.StatusCode);
        }
        var g = current.Governance;
        return await UpsertAsync(datasetId, new UpsertDatasetGovernanceRequest(request.DataStewardId,
            g?.BusinessDescription ?? current.Description, EffectiveGovernanceStatus(g),
            g?.Classification ?? DataClassifications.Internal, g?.LastReviewedDateUtc, g?.NextReviewDateUtc), ct);
    }

    public async Task<ServiceResult<DatasetCertificationResponse>> SubmitCertificationAsync(Guid datasetId, SubmitCertificationRequest request, CancellationToken ct)
    {
        var authorization = await access.AuthorizeAsync(datasetId, DatasetAccessLevels.Manage, ct);
        if (!authorization.Succeeded) return Fail<DatasetCertificationResponse>(authorization);
        if (await db.DatasetCertifications.AnyAsync(x => x.DatasetId == datasetId && x.CertificationStatus == CertificationStatuses.Pending, ct))
            return ServiceResult<DatasetCertificationResponse>.Failure("A certification request is already pending for this dataset.", 409);

        var dataset = await GovernanceDatasetQuery().SingleAsync(x => x.Id == datasetId, ct);
        var governance = await EnsureGovernanceAsync(dataset, ct);
        var currentStatus = EffectiveGovernanceStatus(governance);
        if (!CanTransition(currentStatus, GovernanceStatuses.UnderReview, allowCertification: true))
            return ServiceResult<DatasetCertificationResponse>.Failure($"A dataset in '{currentStatus}' status cannot be submitted for certification.", 409);

        var certification = new DatasetCertification
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = datasetId,
            SubmittedByUserId = currentUser.UserId!.Value, Comments = Clean(request.Comments)
        };
        governance.GovernanceStatus = GovernanceStatuses.UnderReview;
        governance.UpdatedByUserId = currentUser.UserId.Value;
        governance.UpdatedAtUtc = DateTime.UtcNow;
        db.DatasetCertifications.Add(certification);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetCertificationSubmitted", "DatasetCertification", certification.Id.ToString(),
            $"Dataset {dataset.Code} submitted for certification.", workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetCertificationResponse>.Success(ToCertification(certification), 201);
    }

    public async Task<ServiceResult<DatasetCertificationResponse>> ReviewCertificationAsync(Guid certificationId, ReviewCertificationRequest request, CancellationToken ct)
    {
        var certification = await db.DatasetCertifications.Include(x => x.Dataset).ThenInclude(x => x.Governance)
            .SingleOrDefaultAsync(x => x.Id == certificationId, ct);
        if (certification is null) return ServiceResult<DatasetCertificationResponse>.Failure("Certification request was not found.", 404);
        if (!await access.IsDatasetReviewerAsync(certification.DatasetId, ct))
            return ServiceResult<DatasetCertificationResponse>.Failure("Only the data owner, data steward, or an administrator can review certification.", 403);
        if (certification.CertificationStatus != CertificationStatuses.Pending)
            return ServiceResult<DatasetCertificationResponse>.Failure("The certification request has already been reviewed.", 409);
        if (request.Approve && (!request.ReviewExpiryDateUtc.HasValue || request.ReviewExpiryDateUtc <= DateTime.UtcNow))
            return ServiceResult<DatasetCertificationResponse>.Failure("A future review/expiry date is required when certifying a dataset.", 400);

        var now = DateTime.UtcNow;
        certification.CertifiedByUserId = currentUser.UserId;
        certification.ReviewedAtUtc = now;
        certification.Comments = Clean(request.Comments) ?? certification.Comments;
        certification.CertificationStatus = request.Approve ? CertificationStatuses.Certified : CertificationStatuses.Rejected;
        certification.CertifiedDateUtc = request.Approve ? now : null;
        certification.ReviewExpiryDateUtc = request.Approve ? request.ReviewExpiryDateUtc : null;
        var governance = certification.Dataset.Governance ?? await EnsureGovernanceAsync(certification.Dataset, ct);
        governance.GovernanceStatus = request.Approve ? GovernanceStatuses.Certified : GovernanceStatuses.Draft;
        governance.LastReviewedDateUtc = now;
        governance.NextReviewDateUtc = request.Approve ? request.ReviewExpiryDateUtc : null;
        governance.UpdatedByUserId = currentUser.UserId!.Value;
        governance.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(request.Approve ? "DatasetCertified" : "DatasetCertificationRejected", "DatasetCertification",
            certification.Id.ToString(), Clean(request.Comments), workspaceId: certification.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetCertificationResponse>.Success(ToCertification(certification));
    }

    public async Task<ServiceResult<IReadOnlyList<DatasetCertificationResponse>>> CertificationHistoryAsync(Guid datasetId, CancellationToken ct)
    {
        var authorization = await access.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (!authorization.Succeeded) return Fail<IReadOnlyList<DatasetCertificationResponse>>(authorization);
        var rows = await db.DatasetCertifications.AsNoTracking().Where(x => x.DatasetId == datasetId)
            .OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<DatasetCertificationResponse>>.Success(rows.Select(ToCertification).ToList());
    }

    public async Task<ServiceResult<DatasetAccessRequestResponse>> RequestAccessAsync(Guid datasetId, CreateDatasetAccessRequest request, CancellationToken ct)
    {
        var level = Canonical(request.RequestedAccessLevel, DatasetAccessLevels.All);
        if (level is null) return ServiceResult<DatasetAccessRequestResponse>.Failure("Access level must be Read, Write, or Manage.", 400);
        if (!currentUser.UserId.HasValue || !currentUser.WorkspaceId.HasValue)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Authentication and workspace membership are required.", 401);
        if (request.RequestedExpiryDateUtc.HasValue && request.RequestedExpiryDateUtc <= DateTime.UtcNow)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Requested expiry date must be in the future.", 400);

        var dataset = await db.Datasets.IgnoreQueryFilters().Include(x => x.Governance)
            .SingleOrDefaultAsync(x => x.Id == datasetId && !x.IsDeleted && x.WorkspaceId == currentUser.WorkspaceId, ct);
        if (dataset is null) return ServiceResult<DatasetAccessRequestResponse>.Failure("Dataset was not found.", 404);
        var classification = dataset.Governance?.Classification ?? DataClassifications.Internal;
        if (!DataClassifications.RequiresGrant(classification))
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Public and Internal datasets do not require a separate dataset access grant.", 409);
        var alreadyAuthorized = await access.AuthorizeAsync(datasetId, level, ct);
        if (alreadyAuthorized.Succeeded)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("You already have the requested dataset access.", 409);
        if (await db.DatasetAccessRequests.AnyAsync(x => x.DatasetId == datasetId && x.RequestingUserId == currentUser.UserId &&
            x.RequestedAccessLevel == level && x.Status == AccessRequestStatuses.Pending, ct))
            return ServiceResult<DatasetAccessRequestResponse>.Failure("An equivalent access request is already pending.", 409);

        var entity = new DatasetAccessRequest
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = datasetId, RequestingUserId = currentUser.UserId.Value,
            RequestedAccessLevel = level, BusinessJustification = request.BusinessJustification.Trim(),
            RequestedExpiryDateUtc = request.RequestedExpiryDateUtc
        };
        db.DatasetAccessRequests.Add(entity);
        AddHistory(entity, "Submitted", string.Empty, AccessRequestStatuses.Pending, request.BusinessJustification);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetAccessRequested", "DatasetAccessRequest", entity.Id.ToString(),
            $"{level} access requested for dataset {dataset.Code}.", workspaceId: dataset.WorkspaceId, cancellationToken: ct);
        var loaded = await AccessRequestQuery().SingleAsync(x => x.Id == entity.Id, ct);
        return ServiceResult<DatasetAccessRequestResponse>.Success(ToAccessRequest(loaded), 201);
    }

    public async Task<ServiceResult<DatasetAccessRequestResponse>> ReviewAccessAsync(Guid requestId, ReviewAccessRequest request, CancellationToken ct)
    {
        var entity = await AccessRequestQuery().SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (entity is null) return ServiceResult<DatasetAccessRequestResponse>.Failure("Access request was not found.", 404);
        if (!await access.IsDatasetReviewerAsync(entity.DatasetId, ct))
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Only the data owner, data steward, or an administrator can review this request.", 403);
        if (entity.RequestingUserId == currentUser.UserId)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Users cannot approve or reject their own access requests.", 403);
        if (entity.Status != AccessRequestStatuses.Pending)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("The access request has already been reviewed.", 409);
        var expiry = request.ExpiryDateUtc ?? entity.RequestedExpiryDateUtc;
        if (request.Approve && expiry.HasValue && expiry <= DateTime.UtcNow)
            return ServiceResult<DatasetAccessRequestResponse>.Failure("Grant expiry date must be in the future.", 400);

        var now = DateTime.UtcNow;
        var oldStatus = entity.Status;
        entity.Status = request.Approve ? AccessRequestStatuses.Approved : AccessRequestStatuses.Rejected;
        entity.ReviewerId = currentUser.UserId;
        entity.ReviewComments = Clean(request.Comments);
        entity.ReviewedDateUtc = now;
        if (request.Approve)
        {
            var existing = await db.DatasetAccessGrants.SingleOrDefaultAsync(x => x.DatasetId == entity.DatasetId &&
                x.UserId == entity.RequestingUserId && x.Status == AccessGrantStatuses.Active, ct);
            if (existing is null)
            {
                db.DatasetAccessGrants.Add(new DatasetAccessGrant
                {
                    WorkspaceId = entity.WorkspaceId, DatasetId = entity.DatasetId, UserId = entity.RequestingUserId,
                    AccessLevel = entity.RequestedAccessLevel, GrantedByUserId = currentUser.UserId!.Value,
                    GrantedDateUtc = now, ExpiryDateUtc = expiry, SourceAccessRequestId = entity.Id
                });
            }
            else
            {
                if (DatasetAccessLevels.Rank(entity.RequestedAccessLevel) > DatasetAccessLevels.Rank(existing.AccessLevel))
                    existing.AccessLevel = entity.RequestedAccessLevel;
                existing.GrantedByUserId = currentUser.UserId!.Value;
                existing.GrantedDateUtc = now;
                existing.ExpiryDateUtc = expiry;
                existing.SourceAccessRequestId = entity.Id;
            }
        }
        AddHistory(entity, request.Approve ? "Approved" : "Rejected", oldStatus, entity.Status, request.Comments);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(request.Approve ? "DatasetAccessApproved" : "DatasetAccessRejected", "DatasetAccessRequest",
            entity.Id.ToString(), Clean(request.Comments), workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetAccessRequestResponse>.Success(ToAccessRequest(entity));
    }

    public async Task<ServiceResult<DatasetAccessGrantResponse>> RevokeAccessAsync(Guid accessRequestId, RevokeDatasetAccessRequest request, CancellationToken ct)
    {
        var grant = await db.DatasetAccessGrants.Include(x => x.SourceAccessRequest)
            .SingleOrDefaultAsync(x => x.SourceAccessRequestId == accessRequestId && x.Status == AccessGrantStatuses.Active, ct);
        if (grant is null) return ServiceResult<DatasetAccessGrantResponse>.Failure("Access grant was not found.", 404);
        if (!await access.IsDatasetReviewerAsync(grant.DatasetId, ct))
            return ServiceResult<DatasetAccessGrantResponse>.Failure("Only the data owner, data steward, or an administrator can revoke access.", 403);
        if (grant.Status != AccessGrantStatuses.Active)
            return ServiceResult<DatasetAccessGrantResponse>.Failure("The access grant is not active.", 409);
        var now = DateTime.UtcNow;
        grant.Status = AccessGrantStatuses.Revoked;
        grant.RevokedByUserId = currentUser.UserId;
        grant.RevokedDateUtc = now;
        var old = grant.SourceAccessRequest.Status;
        grant.SourceAccessRequest.Status = AccessRequestStatuses.Revoked;
        AddHistory(grant.SourceAccessRequest, "Revoked", old, AccessRequestStatuses.Revoked, request.Reason);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("DatasetAccessRevoked", "DatasetAccessGrant", grant.Id.ToString(), request.Reason.Trim(),
            workspaceId: grant.WorkspaceId, cancellationToken: ct);
        return ServiceResult<DatasetAccessGrantResponse>.Success(ToGrant(grant));
    }

    public async Task<ServiceResult<PagedResponse<DatasetAccessRequestResponse>>> AccessRequestsAsync(AccessRequestSearchRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Status) && Canonical(request.Status, AccessRequestStatuses.All) is null)
            return ServiceResult<PagedResponse<DatasetAccessRequestResponse>>.Failure("Invalid access-request status.", 400);
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);
        var query = AccessRequestQuery().AsNoTracking();
        if (!await access.IsWorkspaceAdministratorAsync(ct) && !currentUser.IsPlatformAdministrator)
        {
            var userId = currentUser.UserId;
            query = query.Where(x => x.RequestingUserId == userId || x.Dataset.OwnerId == userId ||
                (x.Dataset.Governance != null && x.Dataset.Governance.DataStewardId == userId));
        }
        if (request.DatasetId.HasValue) query = query.Where(x => x.DatasetId == request.DatasetId);
        if (request.RequestingUserId.HasValue) query = query.Where(x => x.RequestingUserId == request.RequestingUserId);
        if (!string.IsNullOrWhiteSpace(request.Status)) { var status = Canonical(request.Status, AccessRequestStatuses.All)!; query = query.Where(x => x.Status == status); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RequestDateUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return ServiceResult<PagedResponse<DatasetAccessRequestResponse>>.Success(new(rows.Select(ToAccessRequest).ToList(), page, size, total));
    }

    public async Task<ServiceResult<IReadOnlyList<DatasetAccessHistoryResponse>>> AccessHistoryAsync(Guid requestId, CancellationToken ct)
    {
        var request = await db.DatasetAccessRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (request is null) return ServiceResult<IReadOnlyList<DatasetAccessHistoryResponse>>.Failure("Access request was not found.", 404);
        if (request.RequestingUserId != currentUser.UserId && !await access.IsDatasetReviewerAsync(request.DatasetId, ct))
            return ServiceResult<IReadOnlyList<DatasetAccessHistoryResponse>>.Failure("You are not authorized to view this request history.", 403);
        var rows = await db.DatasetAccessHistory.AsNoTracking().Where(x => x.AccessRequestId == requestId)
            .OrderBy(x => x.PerformedAtUtc).Select(x => new DatasetAccessHistoryResponse(x.Id, x.AccessRequestId, x.Action,
                x.FromStatus, x.ToStatus, x.PerformedByUserId, x.Comments, x.PerformedAtUtc)).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<DatasetAccessHistoryResponse>>.Success(rows);
    }

    public async Task<ServiceResult<PagedResponse<DatasetGovernanceResponse>>> SearchAsync(GovernanceSearchRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Classification) && Canonical(request.Classification, DataClassifications.All) is null)
            return ServiceResult<PagedResponse<DatasetGovernanceResponse>>.Failure("Invalid classification.", 400);
        if (!string.IsNullOrWhiteSpace(request.GovernanceStatus) && Canonical(request.GovernanceStatus, GovernanceStatuses.All) is null)
            return ServiceResult<PagedResponse<DatasetGovernanceResponse>>.Failure("Invalid governance status.", 400);
        var page = Math.Max(1, request.Page); var size = Math.Clamp(request.PageSize, 1, 100);
        var query = GovernanceDatasetQuery().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword)) { var term = request.Keyword.Trim(); query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term) || (x.Governance != null && x.Governance.BusinessDescription.Contains(term))); }
        if (!string.IsNullOrWhiteSpace(request.Classification)) { var c = Canonical(request.Classification, DataClassifications.All)!; query = query.Where(x => (x.Governance == null ? DataClassifications.Internal : x.Governance.Classification) == c); }
        if (!string.IsNullOrWhiteSpace(request.GovernanceStatus)) { var s = Canonical(request.GovernanceStatus, GovernanceStatuses.All)!; query = query.Where(x => (x.Governance == null ? GovernanceStatuses.Draft : x.Governance.GovernanceStatus) == s); }
        if (request.OwnerId.HasValue) query = query.Where(x => x.OwnerId == request.OwnerId);
        if (request.StewardId.HasValue) query = query.Where(x => x.Governance != null && x.Governance.DataStewardId == request.StewardId);
        if (request.ReviewOverdue.HasValue) { var now = DateTime.UtcNow; query = request.ReviewOverdue.Value ? query.Where(x => x.Governance != null && x.Governance.NextReviewDateUtc <= now) : query.Where(x => x.Governance == null || !x.Governance.NextReviewDateUtc.HasValue || x.Governance.NextReviewDateUtc > now); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Governance == null ? x.UpdatedAtUtc : x.Governance.UpdatedAtUtc)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var visible = new List<DatasetGovernanceResponse>();
        foreach (var row in rows)
            if ((await access.AuthorizeAsync(row.Id, DatasetAccessLevels.Read, ct)).Succeeded) visible.Add(ToGovernance(row));
        return ServiceResult<PagedResponse<DatasetGovernanceResponse>>.Success(new(visible, page, size, total));
    }

    public async Task<ServiceResult<GovernanceDashboardResponse>> DashboardAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var datasets = GovernanceDatasetQuery().AsNoTracking();
        var total = await datasets.CountAsync(ct);
        var certified = await datasets.CountAsync(x => x.Governance != null && x.Governance.GovernanceStatus == GovernanceStatuses.Certified && (!x.Governance.NextReviewDateUtc.HasValue || x.Governance.NextReviewDateUtc > now), ct);
        var pendingReview = await datasets.CountAsync(x => x.Governance != null && x.Governance.GovernanceStatus == GovernanceStatuses.UnderReview, ct);
        var expired = await datasets.CountAsync(x => x.Governance != null && (x.Governance.GovernanceStatus == GovernanceStatuses.Expired || (x.Governance.NextReviewDateUtc.HasValue && x.Governance.NextReviewDateUtc <= now)), ct);
        var classes = await datasets.GroupBy(x => x.Governance == null ? DataClassifications.Internal : x.Governance.Classification)
            .Select(x => new ClassificationCount(x.Key, x.Count())).ToListAsync(ct);
        var pendingRequests = await db.DatasetAccessRequests.CountAsync(x => x.Status == AccessRequestStatuses.Pending, ct);
        var recent = await db.DatasetAccessRequests.AsNoTracking().Where(x => x.ReviewedDateUtc.HasValue &&
                (x.Status == AccessRequestStatuses.Approved || x.Status == AccessRequestStatuses.Rejected))
            .OrderByDescending(x => x.ReviewedDateUtc).Take(10)
            .Select(x => new RecentAccessDecision(x.Id, x.DatasetId, x.Dataset.Name, x.RequestingUserId, x.Status, x.ReviewedDateUtc!.Value)).ToListAsync(ct);
        return ServiceResult<GovernanceDashboardResponse>.Success(new(total, certified, pendingReview, expired, classes, pendingRequests, recent));
    }

    private IQueryable<Dataset> GovernanceDatasetQuery() => db.Datasets
        .Include(x => x.Owner).Include(x => x.Governance).ThenInclude(x => x!.DataSteward);
    private IQueryable<DatasetAccessRequest> AccessRequestQuery() => db.DatasetAccessRequests
        .Include(x => x.Dataset).ThenInclude(x => x.Governance).Include(x => x.RequestingUser).Include(x => x.Reviewer);

    private async Task<string?> ValidateGovernanceAsync(Guid datasetId, Guid? stewardId, string classification,
        string status, DateTime? last, DateTime? next, CancellationToken ct)
    {
        if (Canonical(classification, DataClassifications.All) is null) return "Classification must be Public, Internal, Confidential, or Restricted.";
        if (Canonical(status, GovernanceStatuses.All) is null) return "Invalid governance status.";
        if (last.HasValue && last > DateTime.UtcNow) return "Last reviewed date cannot be in the future.";
        if (last.HasValue && next.HasValue && next <= last) return "Next review date must be later than the last reviewed date.";
        if (stewardId.HasValue)
        {
            var workspace = await db.Datasets.IgnoreQueryFilters().Where(x => x.Id == datasetId).Select(x => x.WorkspaceId).SingleAsync(ct);
            if (!await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == stewardId && x.WorkspaceId == workspace && x.IsActive, ct))
                return "Data steward must be an active user in the same workspace.";
        }
        return null;
    }

    private async Task<DatasetGovernance> EnsureGovernanceAsync(Dataset dataset, CancellationToken ct)
    {
        if (dataset.Governance is not null) return dataset.Governance;
        var entity = new DatasetGovernance
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = dataset.Id,
            BusinessDescription = dataset.Description, UpdatedByUserId = currentUser.UserId!.Value
        };
        db.DatasetGovernance.Add(entity);
        dataset.Governance = entity;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    private void AddHistory(DatasetAccessRequest request, string action, string from, string to, string? comments) =>
        db.DatasetAccessHistory.Add(new DatasetAccessHistory
        {
            WorkspaceId = request.WorkspaceId, DatasetId = request.DatasetId, AccessRequestId = request.Id,
            Action = action, FromStatus = from, ToStatus = to, PerformedByUserId = currentUser.UserId!.Value,
            Comments = Clean(comments)
        });

    private static bool CanTransition(string from, string to, bool allowCertification)
    {
        if (from == to) return true;
        if (to == GovernanceStatuses.Certified && !allowCertification) return false;
        return from switch
        {
            GovernanceStatuses.Draft => to is GovernanceStatuses.UnderReview or GovernanceStatuses.Archived,
            GovernanceStatuses.UnderReview => to is GovernanceStatuses.Draft or GovernanceStatuses.Certified or GovernanceStatuses.Archived,
            GovernanceStatuses.Certified => to is GovernanceStatuses.UnderReview or GovernanceStatuses.Expired or GovernanceStatuses.Archived,
            GovernanceStatuses.Expired => to is GovernanceStatuses.UnderReview or GovernanceStatuses.Archived,
            GovernanceStatuses.Archived => to == GovernanceStatuses.Draft,
            _ => false
        };
    }

    private static string EffectiveGovernanceStatus(DatasetGovernance? value) => value is null ? GovernanceStatuses.Draft :
        value.GovernanceStatus == GovernanceStatuses.Certified && value.NextReviewDateUtc.HasValue && value.NextReviewDateUtc <= DateTime.UtcNow
            ? GovernanceStatuses.Expired : value.GovernanceStatus;
    private static string? Canonical(string? value, IEnumerable<string> values) =>
        values.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ServiceResult<T> Fail<T>(ServiceResult<bool> result) => ServiceResult<T>.Failure(result.Error!, result.StatusCode);

    private static DatasetGovernanceResponse ToGovernance(Dataset x)
    {
        var g = x.Governance;
        return new(x.Id, x.Code, x.Name, x.WorkspaceId, x.OwnerId, x.Owner.FullName,
            g?.DataStewardId, g?.DataSteward?.FullName, g?.BusinessDescription ?? x.Description,
            EffectiveGovernanceStatus(g), g?.Classification ?? DataClassifications.Internal,
            g?.LastReviewedDateUtc, g?.NextReviewDateUtc, g?.CreatedAtUtc ?? x.CreatedAtUtc, g?.UpdatedAtUtc ?? x.UpdatedAtUtc);
    }
    private static DatasetCertificationResponse ToCertification(DatasetCertification x) => new(x.Id, x.DatasetId,
        x.SubmittedByUserId, x.SubmittedAtUtc, x.CertifiedByUserId, x.CertifiedDateUtc,
        x.CertificationStatus == CertificationStatuses.Certified && x.ReviewExpiryDateUtc.HasValue && x.ReviewExpiryDateUtc <= DateTime.UtcNow ? CertificationStatuses.Expired : x.CertificationStatus,
        x.ReviewExpiryDateUtc, x.Comments, x.ReviewedAtUtc);
    private static DatasetAccessRequestResponse ToAccessRequest(DatasetAccessRequest x) => new(x.Id, x.DatasetId,
        x.Dataset.Name, x.RequestingUserId, x.RequestingUser.FullName, x.RequestedAccessLevel,
        x.BusinessJustification, x.RequestDateUtc, x.Status, x.ReviewerId, x.Reviewer?.FullName,
        x.ReviewComments, x.ReviewedDateUtc, x.RequestedExpiryDateUtc);
    private static DatasetAccessGrantResponse ToGrant(DatasetAccessGrant x) => new(x.Id, x.DatasetId, x.UserId,
        x.AccessLevel, x.GrantedByUserId, x.GrantedDateUtc, x.ExpiryDateUtc,
        x.Status == AccessGrantStatuses.Active && x.ExpiryDateUtc.HasValue && x.ExpiryDateUtc <= DateTime.UtcNow
            ? AccessGrantStatuses.Expired : x.Status,
        x.RevokedByUserId, x.RevokedDateUtc, x.SourceAccessRequestId);
}
