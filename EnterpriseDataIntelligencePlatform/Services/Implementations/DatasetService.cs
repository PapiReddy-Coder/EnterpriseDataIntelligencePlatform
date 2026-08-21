using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class DatasetService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuditService audit) : IDatasetService
{
    public async Task<ServiceResult<DatasetDetailsResponse>> RegisterAsync(
        RegisterDatasetRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspaceResult = await ResolveWorkspaceAsync(request.WorkspaceId, cancellationToken);
        if (!workspaceResult.Succeeded)
            return ServiceResult<DatasetDetailsResponse>.Failure(workspaceResult.Error!, workspaceResult.StatusCode);

        var workspaceId = workspaceResult.Value;
        var validation = await ValidateMetadataAsync(workspaceId, request.Name, request.CategoryId, request.OwnerId, null, request.Tags, cancellationToken);
        if (!validation.Succeeded)
            return ServiceResult<DatasetDetailsResponse>.Failure(validation.Error!, validation.StatusCode);

        var access = await EnsureCanCreateAsync(request.OwnerId, cancellationToken);
        if (!access.Succeeded)
            return ServiceResult<DatasetDetailsResponse>.Failure(access.Error!, access.StatusCode);

        if (!currentUser.UserId.HasValue)
            return ServiceResult<DatasetDetailsResponse>.Failure("Authenticated user ID is missing from the access token.", StatusCodes.Status401Unauthorized);

        var createdBy = currentUser.UserId.Value;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var dataset = new Dataset
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    Code = await GenerateDatasetCodeAsync(cancellationToken),
                    Name = request.Name.Trim(),
                    NormalizedName = Normalize(request.Name),
                    Description = request.Description?.Trim() ?? string.Empty,
                    CategoryId = request.CategoryId,
                    OwnerId = request.OwnerId,
                    DataSourceName = request.DataSourceName.Trim(),
                    DataSourceType = request.DataSourceType.Trim(),
                    DataSourceDescription = request.DataSourceDescription?.Trim() ?? string.Empty,
                    Status = DatasetStatuses.Draft,
                    CurrentVersion = 1,
                    IsDeleted = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                db.Datasets.Add(dataset);
                await SetTagsAsync(dataset, request.Tags, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await LoadReferencesAsync(dataset, cancellationToken);
                await CreateVersionAsync(dataset, request.VersionNotes ?? "Initial dataset registration", createdBy);
                await db.SaveChangesAsync(cancellationToken);

                await audit.WriteAsync(
                    action: "DatasetCreated",
                    entityType: "Dataset",
                    entityId: dataset.Id.ToString(),
                    details: $"Dataset {dataset.Code} '{dataset.Name}' created at version 1.",
                    workspaceId: dataset.WorkspaceId,
                    cancellationToken: cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return ServiceResult<DatasetDetailsResponse>.Success(ToDetails(dataset), StatusCodes.Status201Created);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<ServiceResult<DatasetDetailsResponse>> UpdateAsync(
        Guid id,
        UpdateDatasetRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataset = await QueryEditable().Include(x => x.DatasetTags).ThenInclude(x => x.Tag)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataset is null || dataset.IsDeleted)
            return ServiceResult<DatasetDetailsResponse>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        if (dataset.Status == DatasetStatuses.Archived)
            return ServiceResult<DatasetDetailsResponse>.Failure("An archived dataset must be restored before metadata can be updated.", StatusCodes.Status409Conflict);

        var access = await EnsureCanUpdateAsync(dataset, request.OwnerId, cancellationToken);
        if (!access.Succeeded)
            return ServiceResult<DatasetDetailsResponse>.Failure(access.Error!, access.StatusCode);

        var validation = await ValidateMetadataAsync(dataset.WorkspaceId, request.Name, request.CategoryId, request.OwnerId, dataset.Id, request.Tags, cancellationToken);
        if (!validation.Succeeded)
            return ServiceResult<DatasetDetailsResponse>.Failure(validation.Error!, validation.StatusCode);

        if (!currentUser.UserId.HasValue)
            return ServiceResult<DatasetDetailsResponse>.Failure("Authenticated user ID is missing from the access token.", StatusCodes.Status401Unauthorized);

        var normalizedTags = NormalizeTags(request.Tags);
        var existingTags = dataset.DatasetTags.Select(x => Normalize(x.Tag.Name)).OrderBy(x => x).ToArray();
        var metadataChanged =
            !string.Equals(dataset.Name, request.Name.Trim(), StringComparison.Ordinal) ||
            !string.Equals(dataset.Description, request.Description?.Trim() ?? string.Empty, StringComparison.Ordinal) ||
            dataset.CategoryId != request.CategoryId ||
            dataset.OwnerId != request.OwnerId ||
            !string.Equals(dataset.DataSourceName, request.DataSourceName.Trim(), StringComparison.Ordinal) ||
            !string.Equals(dataset.DataSourceType, request.DataSourceType.Trim(), StringComparison.Ordinal) ||
            !string.Equals(dataset.DataSourceDescription, request.DataSourceDescription?.Trim() ?? string.Empty, StringComparison.Ordinal) ||
            !existingTags.SequenceEqual(normalizedTags.Select(Normalize).OrderBy(x => x));

        if (!metadataChanged)
        {
            await LoadReferencesAsync(dataset, cancellationToken);
            return ServiceResult<DatasetDetailsResponse>.Success(ToDetails(dataset));
        }

        var changedBy = currentUser.UserId.Value;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await MarkVersionsNotCurrentAsync(dataset.Id, cancellationToken);
                dataset.Name = request.Name.Trim();
                dataset.NormalizedName = Normalize(request.Name);
                dataset.Description = request.Description?.Trim() ?? string.Empty;
                dataset.CategoryId = request.CategoryId;
                dataset.OwnerId = request.OwnerId;
                dataset.DataSourceName = request.DataSourceName.Trim();
                dataset.DataSourceType = request.DataSourceType.Trim();
                dataset.DataSourceDescription = request.DataSourceDescription?.Trim() ?? string.Empty;
                dataset.CurrentVersion++;
                dataset.UpdatedAtUtc = DateTime.UtcNow;
                await SetTagsAsync(dataset, request.Tags, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await LoadReferencesAsync(dataset, cancellationToken);
                await CreateVersionAsync(dataset, request.VersionNotes ?? "Dataset metadata updated", changedBy);
                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync(
                    action: "DatasetUpdated",
                    entityType: "Dataset",
                    entityId: dataset.Id.ToString(),
                    details: $"Dataset {dataset.Code} metadata updated to version {dataset.CurrentVersion}.",
                    workspaceId: dataset.WorkspaceId,
                    cancellationToken: cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return ServiceResult<DatasetDetailsResponse>.Success(ToDetails(dataset));
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<ServiceResult<DatasetDetailsResponse>> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        if (includeDeleted && !await IsAdministratorAsync(cancellationToken))
            return ServiceResult<DatasetDetailsResponse>.Failure("Only administrators can view soft-deleted datasets.", StatusCodes.Status403Forbidden);

        var query = includeDeleted ? QueryEditable() : db.Datasets.AsQueryable();
        var dataset = await query.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Owner)
            .Include(x => x.DatasetTags).ThenInclude(x => x.Tag)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataset is null)
            return ServiceResult<DatasetDetailsResponse>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        if (!await CanViewStatusAsync(dataset.Status, cancellationToken))
            return ServiceResult<DatasetDetailsResponse>.Failure("The dataset is not available in the catalog.", StatusCodes.Status403Forbidden);
        return ServiceResult<DatasetDetailsResponse>.Success(ToDetails(dataset));
    }

    public async Task<ServiceResult<PagedResponse<DatasetCatalogItemResponse>>> SearchAsync(DatasetSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CreatedFromUtc.HasValue && request.CreatedToUtc.HasValue && request.CreatedFromUtc > request.CreatedToUtc)
            return ServiceResult<PagedResponse<DatasetCatalogItemResponse>>.Failure("CreatedFromUtc cannot be later than CreatedToUtc.", StatusCodes.Status400BadRequest);
        if (!string.IsNullOrWhiteSpace(request.Status) && !DatasetStatuses.All.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            return ServiceResult<PagedResponse<DatasetCatalogItemResponse>>.Failure("Invalid dataset status. Allowed values are Draft, Active, Archived.", StatusCodes.Status400BadRequest);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var canViewNonActive = await CanViewNonActiveAsync(cancellationToken);
        var query = db.Datasets.AsNoTracking()
            .Include(x => x.Category).Include(x => x.Owner)
            .Include(x => x.DatasetTags).ThenInclude(x => x.Tag).AsQueryable();

        if (!canViewNonActive)
            query = query.Where(x => x.Status == DatasetStatuses.Active);

        if (request.WorkspaceId.HasValue)
        {
            if (!currentUser.IsPlatformAdministrator && request.WorkspaceId != currentUser.WorkspaceId)
                return ServiceResult<PagedResponse<DatasetCatalogItemResponse>>.Failure("Cross-workspace filtering is not allowed.", StatusCodes.Status403Forbidden);
            query = query.Where(x => x.WorkspaceId == request.WorkspaceId.Value);
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            query = query.Where(x => x.Name.Contains(name));
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword) || x.Description.Contains(keyword) ||
                x.Category.Name.Contains(keyword) || x.DataSourceName.Contains(keyword) || x.DataSourceType.Contains(keyword) ||
                x.Owner.FullName.Contains(keyword) || x.DatasetTags.Any(dt => dt.Tag.Name.Contains(keyword)));
        }
        if (request.CategoryId.HasValue) query = query.Where(x => x.CategoryId == request.CategoryId.Value);
        if (request.OwnerId.HasValue) query = query.Where(x => x.OwnerId == request.OwnerId.Value);
        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tag = Normalize(request.Tag);
            query = query.Where(x => x.DatasetTags.Any(dt => dt.Tag.NormalizedName == tag));
        }
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = DatasetStatuses.All.Single(x => x.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
            query = query.Where(x => x.Status == status);
        }
        if (request.CreatedFromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= request.CreatedFromUtc.Value);
        if (request.CreatedToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc <= request.CreatedToUtc.Value);

        var total = await query.CountAsync(cancellationToken);
        query = ApplySorting(query, request.SortBy, request.SortDirection);
        var entities = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponse<DatasetCatalogItemResponse>>.Success(
            new PagedResponse<DatasetCatalogItemResponse>(entities.Select(ToCatalogItem).ToList(), page, pageSize, total));
    }

    public Task<ServiceResult<bool>> ActivateAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, DatasetStatuses.Active, DatasetStatuses.Draft, "DatasetActivated", request.Notes, cancellationToken);

    public Task<ServiceResult<bool>> ArchiveAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, DatasetStatuses.Archived, DatasetStatuses.Active, "DatasetArchived", request.Notes, cancellationToken);

    public Task<ServiceResult<bool>> RestoreArchivedAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(id, DatasetStatuses.Active, DatasetStatuses.Archived, "DatasetRestored", request.Notes, cancellationToken);

    public async Task<ServiceResult<bool>> SoftDeleteAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(cancellationToken))
            return ServiceResult<bool>.Failure("Only Platform Administrators and Workspace Administrators can soft delete datasets.", StatusCodes.Status403Forbidden);
        var dataset = await QueryEditable().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataset is null || dataset.IsDeleted)
            return ServiceResult<bool>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        if (!currentUser.UserId.HasValue)
            return ServiceResult<bool>.Failure("Authenticated user ID is missing from the access token.", StatusCodes.Status401Unauthorized);
        dataset.IsDeleted = true;
        dataset.DeletedAtUtc = DateTime.UtcNow;
        dataset.DeletedByUserId = currentUser.UserId.Value;
        dataset.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            action: "DatasetSoftDeleted",
            entityType: "Dataset",
            entityId: dataset.Id.ToString(),
            details: request.Notes ?? $"Dataset {dataset.Code} soft deleted.",
            workspaceId: dataset.WorkspaceId,
            cancellationToken: cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<bool>> RecoverAsync(Guid id, DatasetLifecycleRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsAdministratorAsync(cancellationToken))
            return ServiceResult<bool>.Failure("Only Platform Administrators and Workspace Administrators can recover datasets.", StatusCodes.Status403Forbidden);
        var dataset = await QueryEditable().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dataset is null)
            return ServiceResult<bool>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        if (!dataset.IsDeleted)
            return ServiceResult<bool>.Failure("Dataset is not soft deleted.", StatusCodes.Status409Conflict);
        dataset.IsDeleted = false;
        dataset.DeletedAtUtc = null;
        dataset.DeletedByUserId = null;
        dataset.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            action: "DatasetRecovered",
            entityType: "Dataset",
            entityId: dataset.Id.ToString(),
            details: request.Notes ?? $"Dataset {dataset.Code} recovered.",
            workspaceId: dataset.WorkspaceId,
            cancellationToken: cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<DatasetVersionHistoryResponse>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dataset = await QueryEditable().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (dataset is null)
            return ServiceResult<DatasetVersionHistoryResponse>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        var versions = await db.DatasetVersions.AsNoTracking().Where(x => x.DatasetId == id)
            .OrderByDescending(x => x.VersionNumber).ToListAsync(cancellationToken);
        var response = new DatasetVersionHistoryResponse(dataset.Id, dataset.Code, dataset.Name, dataset.CurrentVersion,
            versions.Count, versions.Select(ToVersion).ToList());
        return ServiceResult<DatasetVersionHistoryResponse>.Success(response);
    }

    public async Task<ServiceResult<DatasetVersionResponse>> GetVersionAsync(Guid id, int versionNumber, CancellationToken cancellationToken = default)
    {
        if (!await QueryEditable().AnyAsync(x => x.Id == id && !x.IsDeleted, cancellationToken))
            return ServiceResult<DatasetVersionResponse>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        var version = await db.DatasetVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DatasetId == id && x.VersionNumber == versionNumber, cancellationToken);
        return version is null
            ? ServiceResult<DatasetVersionResponse>.Failure("Dataset version was not found.", StatusCodes.Status404NotFound)
            : ServiceResult<DatasetVersionResponse>.Success(ToVersion(version));
    }

    private async Task<ServiceResult<Guid>> ResolveWorkspaceAsync(Guid? requestedWorkspaceId, CancellationToken ct)
    {
        var workspaceId = currentUser.IsPlatformAdministrator ? requestedWorkspaceId : currentUser.WorkspaceId;
        if (!workspaceId.HasValue || workspaceId.Value == Guid.Empty)
            return ServiceResult<Guid>.Failure("Workspace is required.", StatusCodes.Status400BadRequest);
        if (!currentUser.IsPlatformAdministrator && requestedWorkspaceId.HasValue && requestedWorkspaceId != currentUser.WorkspaceId)
            return ServiceResult<Guid>.Failure("Cross-workspace dataset registration is not allowed.", StatusCodes.Status403Forbidden);
        var active = await db.Workspaces.AnyAsync(x => x.Id == workspaceId.Value && x.IsActive, ct);
        return active ? ServiceResult<Guid>.Success(workspaceId.Value)
            : ServiceResult<Guid>.Failure("Workspace was not found or is inactive.", StatusCodes.Status400BadRequest);
    }

    private async Task<ServiceResult<bool>> ValidateMetadataAsync(Guid workspaceId, string name, Guid categoryId, Guid ownerId, Guid? datasetId, IEnumerable<string>? tags, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<bool>.Failure("Dataset name is required.", StatusCodes.Status400BadRequest);
        var duplicateName = await db.Datasets.IgnoreQueryFilters().AnyAsync(x => x.WorkspaceId == workspaceId && !x.IsDeleted && x.NormalizedName == Normalize(name) && (!datasetId.HasValue || x.Id != datasetId.Value), ct);
        if (duplicateName)
            return ServiceResult<bool>.Failure("Dataset name must be unique within the workspace.", StatusCodes.Status409Conflict);
        if (!await db.DatasetCategories.AnyAsync(x => x.Id == categoryId && x.IsActive, ct))
            return ServiceResult<bool>.Failure("An active dataset category is required.", StatusCodes.Status400BadRequest);
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == ownerId && x.WorkspaceId == workspaceId && x.IsActive, ct))
            return ServiceResult<bool>.Failure("Dataset owner must be an active user in the same workspace.", StatusCodes.Status400BadRequest);
        var tagValidation = ValidateTags(tags);
        return tagValidation ?? ServiceResult<bool>.Success(true);
    }

    private static ServiceResult<bool>? ValidateTags(IEnumerable<string>? values)
    {
        if (values is null) return null;
        var tags = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        if (tags.Any(x => x.Length > 100))
            return ServiceResult<bool>.Failure("Each tag must be 100 characters or fewer.", StatusCodes.Status400BadRequest);
        if (tags.Count > 25)
            return ServiceResult<bool>.Failure("A dataset can have a maximum of 25 tags.", StatusCodes.Status400BadRequest);
        if (tags.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return ServiceResult<bool>.Failure("Duplicate tags are not allowed.", StatusCodes.Status400BadRequest);
        return null;
    }

    private async Task<ServiceResult<bool>> EnsureCanCreateAsync(Guid ownerId, CancellationToken ct)
    {
        if (currentUser.IsPlatformAdministrator || await IsInRoleAsync(Roles.WorkspaceAdministrator, ct))
            return ServiceResult<bool>.Success(true);
        if (await IsInRoleAsync(Roles.DataAnalyst, ct))
            return currentUser.UserId == ownerId
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.Failure("Data Analysts can create only datasets assigned to themselves as owner.", StatusCodes.Status403Forbidden);
        return ServiceResult<bool>.Failure("You are not allowed to create datasets.", StatusCodes.Status403Forbidden);
    }

    private async Task<ServiceResult<bool>> EnsureCanUpdateAsync(Dataset dataset, Guid requestedOwnerId, CancellationToken ct)
    {
        if (currentUser.IsPlatformAdministrator || await IsInRoleAsync(Roles.WorkspaceAdministrator, ct))
            return ServiceResult<bool>.Success(true);
        if (await IsInRoleAsync(Roles.DataAnalyst, ct))
        {
            if (!currentUser.UserId.HasValue || dataset.OwnerId != currentUser.UserId.Value)
                return ServiceResult<bool>.Failure("Data Analysts can update only datasets assigned to them as owner.", StatusCodes.Status403Forbidden);
            if (requestedOwnerId != currentUser.UserId.Value)
                return ServiceResult<bool>.Failure("Data Analysts cannot reassign dataset ownership.", StatusCodes.Status403Forbidden);
            return ServiceResult<bool>.Success(true);
        }
        return ServiceResult<bool>.Failure("You are not allowed to update datasets.", StatusCodes.Status403Forbidden);
    }

    private async Task<bool> IsAdministratorAsync(CancellationToken ct) =>
        currentUser.IsPlatformAdministrator || await IsInRoleAsync(Roles.WorkspaceAdministrator, ct);

    private async Task<bool> IsInRoleAsync(string roleName, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return false;
        var userId = currentUser.UserId.Value;
        return await db.UserRoles.Where(x => x.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .AnyAsync(x => x == roleName, ct);
    }

    private async Task<string> GenerateDatasetCodeAsync(
        CancellationToken cancellationToken)
    {
        // SQL Server / relational database
        if (db.Database.IsRelational())
        {
            var connection = db.Database.GetDbConnection();

            var shouldCloseConnection =
                connection.State != ConnectionState.Open;

            if (shouldCloseConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                await using var command = connection.CreateCommand();

                command.CommandText =
                    "SELECT NEXT VALUE FOR dbo.DatasetCodeSequence;";

                command.CommandType = CommandType.Text;

                // Important because RegisterAsync is already inside an EF transaction.
                var currentTransaction =
                    db.Database.CurrentTransaction;

                if (currentTransaction is not null)
                {
                    command.Transaction =
                        currentTransaction.GetDbTransaction();
                }

                var result =
                    await command.ExecuteScalarAsync(cancellationToken);

                if (result is null ||
                    result == DBNull.Value)
                {
                    throw new InvalidOperationException(
                        "Unable to generate the next dataset code.");
                }

                var nextValue = Convert.ToInt64(result);

                return $"DS-{nextValue:D6}";
            }
            finally
            {
                if (shouldCloseConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }

        // Fallback mainly for non-relational/unit-test providers.
        var existingCodes = await db.Datasets
            .IgnoreQueryFilters()
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var nextNumber = existingCodes
            .Select(TryParseDatasetCode)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"DS-{nextNumber:D6}";
    }

    private static int TryParseDatasetCode(string? code) =>
        code is not null && code.StartsWith("DS-", StringComparison.OrdinalIgnoreCase) && int.TryParse(code[3..], out var n) ? n : 0;

    private async Task SetTagsAsync(Dataset dataset, IEnumerable<string>? values, CancellationToken ct)
    {
        var names = NormalizeTags(values);
        if (db.Entry(dataset).State != EntityState.Added)
        {
            var existing = await db.DatasetTags.Where(x => x.DatasetId == dataset.Id).ToListAsync(ct);
            db.DatasetTags.RemoveRange(existing);
        }
        dataset.DatasetTags.Clear();
        foreach (var name in names)
        {
            var normalized = Normalize(name);
            var tag = await db.Tags.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.WorkspaceId == dataset.WorkspaceId && x.NormalizedName == normalized, ct);
            if (tag is null)
            {
                tag = new Tag { WorkspaceId = dataset.WorkspaceId, Name = name, NormalizedName = normalized };
                db.Tags.Add(tag);
            }
            dataset.DatasetTags.Add(new DatasetTag { Dataset = dataset, Tag = tag });
        }
    }

    private static string[] NormalizeTags(IEnumerable<string>? values) =>
        values?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [];

    private async Task LoadReferencesAsync(Dataset dataset, CancellationToken ct)
    {
        await db.Entry(dataset).Reference(x => x.Category).LoadAsync(ct);
        await db.Entry(dataset).Reference(x => x.Owner).LoadAsync(ct);
        await db.Entry(dataset).Collection(x => x.DatasetTags).Query().Include(x => x.Tag).LoadAsync(ct);
    }

    private Task CreateVersionAsync(Dataset dataset, string? notes, Guid createdByUserId)
    {
        var tags = dataset.DatasetTags.Where(x => x.Tag is not null).Select(x => x.Tag.Name).OrderBy(x => x).ToArray();
        db.DatasetVersions.Add(new DatasetVersion
        {
            Id = Guid.NewGuid(), WorkspaceId = dataset.WorkspaceId, DatasetId = dataset.Id,
            VersionNumber = dataset.CurrentVersion, IsCurrent = true, Code = dataset.Code,
            Name = dataset.Name, Description = dataset.Description, CategoryId = dataset.CategoryId,
            CategoryName = dataset.Category.Name, OwnerId = dataset.OwnerId,
            OwnerName = dataset.Owner.FullName ?? dataset.Owner.Email ?? string.Empty,
            DataSourceName = dataset.DataSourceName, DataSourceType = dataset.DataSourceType,
            DataSourceDescription = dataset.DataSourceDescription, Status = dataset.Status,
            TagsJson = JsonSerializer.Serialize(tags), VersionNotes = notes?.Trim(),
            CreatedByUserId = createdByUserId, CreatedAtUtc = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    private async Task MarkVersionsNotCurrentAsync(Guid datasetId, CancellationToken ct)
    {
        var current = await db.DatasetVersions.Where(x => x.DatasetId == datasetId && x.IsCurrent).ToListAsync(ct);
        foreach (var version in current) version.IsCurrent = false;
    }

    private async Task<ServiceResult<bool>> ChangeStatusAsync(Guid id, string newStatus, string requiredStatus, string auditAction, string? notes, CancellationToken ct)
    {
        if (!await IsAdministratorAsync(ct))
            return ServiceResult<bool>.Failure("Only Platform Administrators and Workspace Administrators can change dataset lifecycle status.", StatusCodes.Status403Forbidden);
        var dataset = await QueryEditable().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (dataset is null || dataset.IsDeleted)
            return ServiceResult<bool>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);
        if (dataset.Status != requiredStatus)
            return ServiceResult<bool>.Failure($"Invalid status transition. Dataset must be {requiredStatus} before changing to {newStatus}.", StatusCodes.Status409Conflict);
        dataset.Status = newStatus;
        dataset.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(
            action: auditAction,
            entityType: "Dataset",
            entityId: dataset.Id.ToString(),
            details: notes ?? $"Status changed from {requiredStatus} to {newStatus}. Version remains {dataset.CurrentVersion}.",
            workspaceId: dataset.WorkspaceId,
            cancellationToken: ct);
        return ServiceResult<bool>.Success(true);
    }

    private IQueryable<Dataset> QueryEditable()
    {
        var query = db.Datasets.IgnoreQueryFilters().AsQueryable();
        if (currentUser.IsPlatformAdministrator) return query;
        return currentUser.WorkspaceId.HasValue ? query.Where(x => x.WorkspaceId == currentUser.WorkspaceId.Value) : query.Where(_ => false);
    }

    private async Task<bool> CanViewNonActiveAsync(CancellationToken ct) =>
        currentUser.IsPlatformAdministrator || await IsInRoleAsync(Roles.WorkspaceAdministrator, ct) || await IsInRoleAsync(Roles.DataAnalyst, ct);

    private async Task<bool> CanViewStatusAsync(string status, CancellationToken ct) =>
        status == DatasetStatuses.Active || await CanViewNonActiveAsync(ct);

    private static IQueryable<Dataset> ApplySorting(IQueryable<Dataset> query, string? sortBy, string? direction)
    {
        var desc = !string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
        return (sortBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "name" or "datasetname" => desc ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
            "createdatutc" or "createddate" => desc ? query.OrderByDescending(x => x.CreatedAtUtc) : query.OrderBy(x => x.CreatedAtUtc),
            "updatedatutc" or "lastupdateddate" or "lastmodifieddate" => desc ? query.OrderByDescending(x => x.UpdatedAtUtc) : query.OrderBy(x => x.UpdatedAtUtc),
            _ => query.OrderByDescending(x => x.UpdatedAtUtc)
        };
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static DatasetCatalogItemResponse ToCatalogItem(Dataset x) => new(
        x.Id, x.Code, x.WorkspaceId, x.Name, x.Description, x.CategoryId, x.Category.Name,
        x.OwnerId, x.Owner.FullName, x.DataSourceName, x.DataSourceType, x.Status, x.CurrentVersion,
        x.DatasetTags.Select(t => t.Tag.Name).OrderBy(t => t).ToList(), x.CreatedAtUtc, x.UpdatedAtUtc);

    private static DatasetDetailsResponse ToDetails(Dataset x) => new(
        x.Id, x.Code, x.WorkspaceId, x.Name, x.Description, x.CategoryId, x.Category.Name,
        x.OwnerId, x.Owner.FullName, x.DataSourceName, x.DataSourceType, x.DataSourceDescription,
        x.Status, x.CurrentVersion, x.IsDeleted, x.DatasetTags.Select(t => t.Tag.Name).OrderBy(t => t).ToList(),
        x.CreatedAtUtc, x.UpdatedAtUtc, x.DeletedAtUtc);

    private static DatasetVersionResponse ToVersion(DatasetVersion x) => new(
        x.Id, x.DatasetId, x.Code, x.VersionNumber, x.IsCurrent, x.Name, x.Description,
        x.CategoryId, x.CategoryName, x.OwnerId, x.OwnerName, x.DataSourceName, x.DataSourceType,
        x.DataSourceDescription, x.Status, JsonSerializer.Deserialize<List<string>>(x.TagsJson) ?? [],
        x.VersionNotes, x.CreatedByUserId, x.CreatedAtUtc);
}
