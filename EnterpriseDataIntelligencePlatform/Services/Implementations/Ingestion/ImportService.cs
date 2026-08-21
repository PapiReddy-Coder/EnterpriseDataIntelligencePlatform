using System.Text.Json;
using System.Data;
using EnterpriseDataIntelligencePlatform.Background;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class ImportService(
    AppDbContext db,
    ICurrentUser currentUser,
    IFileStorageService storage,
    IImportFileReader reader,
    IImportJobQueue queue,
    IImportCancellationRegistry cancellations,
    IAuditService audit) : IImportService
{
    public async Task<ServiceResult<FileUploadResponse>> UploadAsync(
        Guid datasetId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null)
            return ServiceResult<FileUploadResponse>.Failure(
                "A file is required.", 400);

        var fileError = ImportRules.ValidateFile(file.FileName, file.Length);

        if (fileError is not null)
            return ServiceResult<FileUploadResponse>.Failure(
                fileError, 400);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        var dataset = await db.Datasets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == datasetId, ct);

        if (dataset is null)
            return ServiceResult<FileUploadResponse>.Failure(
                "Dataset not found or not accessible in the current workspace.",
                404);

        if (dataset.Status == DatasetStatuses.Archived)
            return ServiceResult<FileUploadResponse>.Failure(
                "Archived datasets cannot accept imports.",
                409);

        if (!currentUser.UserId.HasValue)
            return ServiceResult<FileUploadResponse>.Failure(
                "Authenticated user context is required.",
                401);

        string? path = null;

        try
        {
            var saved = await storage.SaveAsync(
                dataset.WorkspaceId,
                dataset.Id,
                file,
                ct);

            path = saved.FullPath;

            var sheets = await reader.GetWorksheetNamesAsync(
                saved.FullPath,
                ext,
                ct);

            var entity = new UploadedDataFile
            {
                DatasetId = dataset.Id,
                WorkspaceId = dataset.WorkspaceId,
                UploadedByUserId = currentUser.UserId.Value,
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = saved.StoredFileName,
                FilePath = saved.FullPath,
                Extension = ext,
                FileSizeBytes = file.Length
            };

            db.UploadedDataFiles.Add(entity);

            await db.SaveChangesAsync(ct);

            await audit.WriteAsync(
                "File Upload",
                "DataImport",
                entity.Id.ToString(),
                $"Uploaded {entity.OriginalFileName} for dataset {dataset.Id}.",
                userId: currentUser.UserId,
                workspaceId: dataset.WorkspaceId,
                cancellationToken: ct);

            return ServiceResult<FileUploadResponse>.Success(
                new(
                    entity.Id,
                    dataset.Id,
                    entity.OriginalFileName,
                    entity.FileSizeBytes,
                    sheets,
                    entity.UploadedAtUtc),
                201);
        }
        catch (Exception ex) when (
            ex is InvalidDataException or IOException)
        {
            if (path is not null)
                await storage.DeleteAsync(path, ct);

            return ServiceResult<FileUploadResponse>.Failure(
                $"File validation failed: {ex.Message}",
                400);
        }
    }

    public async Task<ServiceResult<CreateImportResponse>> CreateAsync(
        Guid datasetId,
        CreateImportRequest request,
        CancellationToken ct)
    {
        if (!ImportModes.All.Contains(
                request.ImportMode,
                StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "ImportMode must be Full or Append.",
                400);
        }

        if (!DuplicateBehaviors.All.Contains(
                request.DuplicateBehavior,
                StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "DuplicateBehavior must be Skip, Update, or Reject.",
                400);
        }

        if (string.IsNullOrEmpty(request.CsvDelimiter) ||
            request.CsvDelimiter.Length != 1)
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "CSV delimiter must be a single character.",
                400);
        }

        if (!currentUser.UserId.HasValue)
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "Authenticated user context is required.",
                401);
        }

        var dataset = await db.Datasets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == datasetId, ct);

        if (dataset is null)
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "Dataset not found or not accessible.",
                404);
        }

        var file = await db.UploadedDataFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == request.FileId &&
                     x.DatasetId == datasetId,
                ct);

        if (file is null)
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "Uploaded file was not found for this dataset.",
                404);
        }

        var filePath = storage.ResolvePath(
            file.WorkspaceId,
            file.DatasetId,
            file.StoredFileName,
            file.FilePath);

        if (!File.Exists(filePath))
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "The uploaded source file is no longer available. Upload the file again and create a new import.",
                409);
        }

        if (file.Extension.Equals(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.WorksheetName))
            {
                return ServiceResult<CreateImportResponse>.Failure(
                    "WorksheetName is required for XLSX imports.",
                    400);
            }

            var sheets = await reader.GetWorksheetNamesAsync(
                filePath,
                file.Extension,
                ct);

            if (!sheets.Contains(
                    request.WorksheetName,
                    StringComparer.OrdinalIgnoreCase))
            {
                return ServiceResult<CreateImportResponse>.Failure(
                    "Selected worksheet does not exist in the uploaded workbook.",
                    400);
            }
        }

        var hasSchema = await db.DatasetColumns
            .AnyAsync(x => x.DatasetId == datasetId, ct);

        if (!hasSchema &&
            !request.ImportMode.Equals(
                ImportModes.Full,
                StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<CreateImportResponse>.Failure(
                "The first successful import must be a Full Import so that the dataset schema can be established.",
                409);
        }

        // CreateImportRequest.KeyColumns is List<string>.
        // Do not use ?? Array.Empty<string>() here because those are
        // different collection types.
        var keys = (request.KeyColumns ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(ImportRules.NormalizeColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Validate requested keys before persisting/queuing the import. Previously
        // this check happened only in the background processor, so the API returned
        // Created/Queued and the import later became Failed.
        if (keys.Length > 0)
        {
            try
            {
                var parsed = await reader.ReadAsync(
                    filePath,
                    file.Extension,
                    request.CsvDelimiter,
                    request.FirstRowContainsHeaders,
                    request.WorksheetName,
                    ct);

                var keyError = ImportRules.ValidateKeyColumns(
                    keys,
                    parsed.Headers);

                if (keyError is not null)
                {
                    return ServiceResult<CreateImportResponse>.Failure(
                        keyError,
                        400);
                }
            }
            catch (Exception ex) when (
                ex is InvalidDataException or IOException)
            {
                return ServiceResult<CreateImportResponse>.Failure(
                    $"File validation failed: {ex.Message}",
                    400);
            }
        }

        var entity = new DataImport
        {
            DatasetId = datasetId,
            WorkspaceId = dataset.WorkspaceId,
            FileId = file.Id,
            InitiatedByUserId = currentUser.UserId.Value,

            ImportMode = Normalize(
                request.ImportMode,
                ImportModes.All),

            DuplicateBehavior = Normalize(
                request.DuplicateBehavior,
                DuplicateBehaviors.All),

            CsvDelimiter = request.CsvDelimiter,
            FirstRowContainsHeaders = request.FirstRowContainsHeaders,
            WorksheetName = request.WorksheetName,
            KeyColumnsJson = JsonSerializer.Serialize(keys)
        };

        var activeConfiguration = await db.DatasetTransformationConfigurations
            .AsNoTracking().FirstOrDefaultAsync(x => x.DatasetId == datasetId && x.IsActive, ct);
        if (activeConfiguration is not null)
        {
            entity.TransformationConfigurationId = activeConfiguration.Id;
            entity.TransformationConfigurationVersion = activeConfiguration.Version;
        }

        db.DataImports.Add(entity);

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            "Import Configuration",
            "DataImport",
            entity.Id.ToString(),
            $"Mode={entity.ImportMode}; Duplicate={entity.DuplicateBehavior}; File={file.OriginalFileName}",
            userId: currentUser.UserId,
            workspaceId: dataset.WorkspaceId,
            cancellationToken: ct);

        return ServiceResult<CreateImportResponse>.Success(
            new(entity.Id, entity.Status),
            201);
    }

    public async Task<ServiceResult<StartImportResponse>> StartAsync(
        Guid importId,
        CancellationToken ct)
    {
        /*
         * SQL Server is configured with EnableRetryOnFailure().
         * EF Core therefore uses SqlServerRetryingExecutionStrategy.
         *
         * A manually started transaction cannot be used directly with
         * that retry strategy. The complete transaction must be executed
         * inside CreateExecutionStrategy().ExecuteAsync().
         */
        var strategy = db.Database.CreateExecutionStrategy();

        StartImportResponse? response = null;
        Guid? initiatedByUserId = null;
        Guid? workspaceId = null;

        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    ct);

            var import = await db.DataImports
                .FirstOrDefaultAsync(
                    x => x.Id == importId,
                    ct);

            if (import is null)
            {
                await transaction.RollbackAsync(ct);

                return ServiceResult<StartImportResponse>.Failure(
                    "Import not found or not accessible.",
                    404);
            }

            if (!ImportRules.CanStart(import.Status))
            {
                await transaction.RollbackAsync(ct);

                return ServiceResult<StartImportResponse>.Failure(
                    $"Import cannot be started from status '{import.Status}'.",
                    409);
            }

            var active = await db.DataImports.AnyAsync(
                x => x.DatasetId == import.DatasetId &&
                     x.Id != import.Id &&
                     (x.Status == ImportStatuses.Queued ||
                      x.Status == ImportStatuses.Processing),
                ct);

            if (active)
            {
                await transaction.RollbackAsync(ct);

                return ServiceResult<StartImportResponse>.Failure(
                    "Another import is already queued or processing for this dataset.",
                    409);
            }

            import.Status = ImportStatuses.Queued;
            import.QueuedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            response = new StartImportResponse(
                import.Id,
                import.Status,
                import.QueuedAtUtc.Value);

            initiatedByUserId = import.InitiatedByUserId;
            workspaceId = import.WorkspaceId;

            return ServiceResult<StartImportResponse>.Success(
                response);
        });

        // Queue only after the database transaction has committed.
        // This prevents a background job from starting against an
        // uncommitted/rolled-back import.
        if (result.Succeeded && response is not null)
        {
            await queue.EnqueueAsync(
                response.ImportId,
                ct);

            await audit.WriteAsync(
                "Import Initiation",
                "DataImport",
                response.ImportId.ToString(),
                "Import queued for background processing.",
                userId: initiatedByUserId,
                workspaceId: workspaceId,
                cancellationToken: ct);
        }

        return result;
    }

    public async Task<ServiceResult<ImportDetailsResponse>> GetAsync(
        Guid importId,
        CancellationToken ct)
    {
        var query = db.DataImports
            .AsNoTracking()
            .Where(x => x.Id == importId);

        var item = await ProjectImportDetails(query)
            .FirstOrDefaultAsync(ct);

        return item is null
            ? ServiceResult<ImportDetailsResponse>.Failure(
                "Import not found or not accessible.",
                404)
            : ServiceResult<ImportDetailsResponse>.Success(

                Map(item));
    }

    public async Task<ServiceResult<PagedResponse<ImportDetailsResponse>>> HistoryAsync(
        Guid? datasetId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // IMPORTANT:
        // Apply all filters to DataImport BEFORE projecting
        // into ImportDetailsProjection.
        var query = db.DataImports
            .AsNoTracking();

        if (datasetId.HasValue)
        {
            query = query.Where(
                x => x.DatasetId == datasetId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(
                x => x.Status == status);
        }

        var count = await query.CountAsync(ct);

        var rows = await ProjectImportDetails(
                query
                    .OrderByDescending(
                        x => x.CreatedAtUtc)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize))
            .ToListAsync(ct);

        var items = rows
            .Select(Map)
            .ToList();

        return ServiceResult<PagedResponse<ImportDetailsResponse>>.Success(
            new(items, page, pageSize, count));
    }

    public async Task<ServiceResult<PagedResponse<ImportErrorResponse>>> ErrorsAsync(
        Guid importId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (!await db.DataImports.AnyAsync(
                x => x.Id == importId,
                ct))
        {
            return ServiceResult<PagedResponse<ImportErrorResponse>>.Failure(
                "Import not found or not accessible.",
                404);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = db.ImportErrors
            .Where(x => x.ImportId == importId);

        var count = await q.CountAsync(ct);

        var items = await q
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.ErrorTimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ImportErrorResponse(
                x.Id,
                x.RowNumber,
                x.ColumnName,
                x.InvalidValue,
                x.ErrorType,
                x.ValidationRule,
                x.ErrorDescription,
                x.ErrorTimestampUtc))
            .ToListAsync(ct);

        return ServiceResult<PagedResponse<ImportErrorResponse>>.Success(
            new(items, page, pageSize, count));
    }

    public async Task<ServiceResult<bool>> CancelAsync(
        Guid importId,
        CancellationToken ct)
    {
        var import = await db.DataImports
            .FirstOrDefaultAsync(
                x => x.Id == importId,
                ct);

        if (import is null)
        {
            return ServiceResult<bool>.Failure(
                "Import not found or not accessible.",
                404);
        }

        if (import.Status == ImportStatuses.Queued)
        {
            import.Status = ImportStatuses.Cancelled;
            import.CancellationRequested = true;
            import.CompletedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }
        else if (import.Status == ImportStatuses.Processing)
        {
            import.CancellationRequested = true;

            await db.SaveChangesAsync(ct);

            cancellations.RequestCancellation(import.Id);
        }
        else
        {
            return ServiceResult<bool>.Failure(
                $"Import cannot be cancelled from status '{import.Status}'.",
                409);
        }

        await audit.WriteAsync(
            "Import Cancellation",
            "DataImport",
            import.Id.ToString(),
            "Cancellation requested.",
            userId: currentUser.UserId,
            workspaceId: import.WorkspaceId,
            cancellationToken: ct);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<IReadOnlyList<DatasetColumnResponse>>> GetSchemaAsync(
        Guid datasetId,
        CancellationToken ct)
    {
        if (!await db.Datasets.AnyAsync(
                x => x.Id == datasetId,
                ct))
        {
            return ServiceResult<IReadOnlyList<DatasetColumnResponse>>.Failure(
                "Dataset not found or not accessible.",
                404);
        }

        var items = await db.DatasetColumns
            .Where(x => x.DatasetId == datasetId)
            .OrderBy(x => x.Ordinal)
            .Select(x => new DatasetColumnResponse(
                x.Id,
                x.Name,
                x.DataType,
                x.Ordinal,
                x.IsRequired,
                x.IsKey))
            .ToListAsync(ct);

        return ServiceResult<IReadOnlyList<DatasetColumnResponse>>.Success(
            items);
    }

    public async Task<ServiceResult<IReadOnlyList<DatasetColumnResponse>>> UpdateKeyColumnsAsync(
        Guid datasetId,
        UpdateDatasetKeyColumnsRequest request,
        CancellationToken ct)
    {
        var columns = await db.DatasetColumns
            .Where(x => x.DatasetId == datasetId)
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);

        if (columns.Count == 0)
        {
            return ServiceResult<IReadOnlyList<DatasetColumnResponse>>.Failure(
                "Dataset schema has not been established yet.",
                409);
        }

        var keys = (request.KeyColumns ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missing = keys
            .Where(k => columns.All(
                c => !c.Name.Equals(
                    k,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missing.Length > 0)
        {
            return ServiceResult<IReadOnlyList<DatasetColumnResponse>>.Failure(
                $"Unknown key column(s): {string.Join(", ", missing)}.",
                400);
        }

        foreach (var column in columns)
        {
            column.IsKey = keys.Contains(
                column.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        await db.SaveChangesAsync(ct);

        return ServiceResult<IReadOnlyList<DatasetColumnResponse>>.Success(
            columns
                .Select(x => new DatasetColumnResponse(
                    x.Id,
                    x.Name,
                    x.DataType,
                    x.Ordinal,
                    x.IsRequired,
                    x.IsKey))
                .ToList());
    }
    private static IQueryable<ImportDetailsProjection> ProjectImportDetails(
    IQueryable<DataImport> query)
    {
        return query.Select(x => new ImportDetailsProjection(
            x.Id,
            x.DatasetId,
            x.FileId,
            x.File.OriginalFileName,
            x.ImportMode,
            x.DuplicateBehavior,
            x.Status,
            x.TotalRecords,
            x.SuccessfullyImportedRecords,
            x.RejectedRecords,
            x.ErrorCount,
            x.CreatedAtUtc,
            x.StartedAtUtc,
            x.CompletedAtUtc,
            x.InitiatedByUserId,
            x.WorksheetName,
            x.KeyColumnsJson,
            x.TransformationConfigurationId,
            x.TransformationConfigurationVersion,
            x.FailureMessage));
    }
    private static ImportDetailsResponse Map(
        ImportDetailsProjection x)
        => new(
            x.ImportId,
            x.DatasetId,
            x.FileId,
            x.FileName,
            x.ImportMode,
            x.DuplicateBehavior,
            x.Status,
            x.TotalRecords,
            x.SuccessfullyImportedRecords,
            x.RejectedRecords,
            x.ErrorCount,
            x.CreatedAtUtc,
            x.StartTimeUtc,
            x.CompletionTimeUtc,
            x.InitiatedBy,
            x.WorksheetName,
            JsonSerializer.Deserialize<string[]>(
                x.KeyColumnsJson) ?? Array.Empty<string>(),
            x.TransformationConfigurationId,
            x.TransformationConfigurationVersion,
            x.FailureMessage);

    private sealed record ImportDetailsProjection(
        Guid ImportId,
        Guid DatasetId,
        Guid FileId,
        string FileName,
        string ImportMode,
        string DuplicateBehavior,
        string Status,
        int TotalRecords,
        int SuccessfullyImportedRecords,
        int RejectedRecords,
        int ErrorCount,
        DateTime CreatedAtUtc,
        DateTime? StartTimeUtc,
        DateTime? CompletionTimeUtc,
        Guid InitiatedBy,
        string? WorksheetName,
        string KeyColumnsJson,
        Guid? TransformationConfigurationId,
        int? TransformationConfigurationVersion,
        string? FailureMessage);

    private static string Normalize(
        string value,
        IEnumerable<string> allowed)
        => allowed.First(x =>
            x.Equals(
                value,
                StringComparison.OrdinalIgnoreCase));
}
