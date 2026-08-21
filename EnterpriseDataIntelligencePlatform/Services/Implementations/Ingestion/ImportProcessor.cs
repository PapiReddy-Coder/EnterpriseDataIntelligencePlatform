using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class ImportProcessor(
    AppDbContext db,
    IImportFileReader reader,
    IFileStorageService storage,
    IImportCancellationRegistry cancellations,
    ITransformationEngine transformationEngine,
    IAuditService audit,
    ILogger<ImportProcessor> logger) : IImportProcessor
{
    public async Task ProcessAsync(
        Guid importId,
        CancellationToken hostToken)
    {
        DataImport? import = null;

        try
        {
            // ============================================================
            // 1. LOAD IMPORT
            // ============================================================

            import = await db.DataImports
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.File)
                .FirstOrDefaultAsync(
                    x => x.Id == importId,
                    hostToken);

            if (import is null)
            {
                logger.LogWarning(
                    "Import {ImportId} was not found.",
                    importId);

                return;
            }

            // ============================================================
            // 2. CANCELLATION CHECK
            // ============================================================

            if (import.CancellationRequested)
            {
                await MarkCancelledAsync(importId);
                return;
            }

            // ============================================================
            // 3. CLAIM IMPORT
            //
            // Created/Queued -> Processing
            //
            // Only one worker is allowed to claim the import.
            // ============================================================

            var claimed = await db.DataImports
                .IgnoreQueryFilters()
                .Where(x =>
                    x.Id == importId &&
                    x.Status == ImportStatuses.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.Status,
                            ImportStatuses.Processing)
                        .SetProperty(
                            x => x.StartedAtUtc,
                            DateTime.UtcNow),
                    hostToken);

            if (claimed != 1)
            {
                logger.LogInformation(
                    "Import {ImportId} was not queued or was already processed.",
                    importId);

                return;
            }

            // ============================================================
            // 4. REGISTER CANCELLATION
            // ============================================================

            var token = cancellations.Register(
                importId,
                hostToken);

            try
            {
                // ========================================================
                // 5. VALIDATE FILE
                // ========================================================

                if (import.File is null)
                {
                    throw new InvalidDataException(
                        "Import file information was not found.");
                }

                // ========================================================
                // 6. READ FILE
                // ========================================================

                var filePath = storage.ResolvePath(
                    import.File.WorkspaceId,
                    import.File.DatasetId,
                    import.File.StoredFileName,
                    import.File.FilePath);

                if (!File.Exists(filePath))
                {
                    throw new InvalidDataException(
                        "The uploaded source file is no longer available. Upload the file again and create a new import.");
                }

                var parsed = await reader.ReadAsync(
                    filePath,
                    import.File.Extension,
                    import.CsvDelimiter,
                    import.FirstRowContainsHeaders,
                    import.WorksheetName,
                    token);

                // ========================================================
                // 7. VALIDATE HEADERS
                // ========================================================

                ValidateHeaders(parsed.Headers);

                // ========================================================
                // 8. UPDATE TOTAL RECORD COUNT
                // ========================================================

                await db.DataImports
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.Id == importId &&
                        x.Status == ImportStatuses.Processing)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            x => x.TotalRecords,
                            parsed.Rows.Count),
                        token);

                // ========================================================
                // 9. LOAD EXISTING DATASET SCHEMA
                // ========================================================

                var existingColumns = await db.DatasetColumns
                    .IgnoreQueryFilters()
                    .Where(x =>
                        x.DatasetId == import.DatasetId)
                    .OrderBy(x => x.Ordinal)
                    .ToListAsync(token);

                // Resolve the immutable configuration captured by this import.
                SaveTransformationConfigurationRequest? transformationConfiguration = null;
                if (import.TransformationConfigurationId.HasValue)
                {
                    var configurationJson = await db.DatasetTransformationConfigurations
                        .IgnoreQueryFilters().Where(x => x.Id == import.TransformationConfigurationId.Value)
                        .Select(x => x.ConfigurationJson).SingleOrDefaultAsync(token);
                    if (configurationJson is null)
                        throw new InvalidDataException("The transformation configuration pinned to this import no longer exists.");
                    transformationConfiguration = JsonSerializer.Deserialize<SaveTransformationConfigurationRequest>(configurationJson,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }

                // ========================================================
                // 10. LOAD KEY COLUMNS
                // ========================================================

                var keyNames = ParseKeyColumns(
                    import.KeyColumnsJson);

                List<DatasetColumn> workingColumns;

                var isFirstSchema =
                    existingColumns.Count == 0;

                // ========================================================
                // 11. FIRST IMPORT / CREATE SCHEMA
                // ========================================================

                if (isFirstSchema)
                {
                    if (!import.ImportMode.Equals(
                            ImportModes.Full,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "The first successful import must be a Full Import.");
                    }

                    var inferredTypes =
                        InferTypes(parsed);

                    workingColumns =
                        parsed.Headers
                            .Select(
                                (header, index) =>
                                    new DatasetColumn
                                    {
                                        DatasetId =
                                            import.DatasetId,

                                        WorkspaceId =
                                            import.WorkspaceId,

                                        Name =
                                            header.Trim(),

                                        NormalizedName =
                                            header.Trim()
                                                .ToUpperInvariant(),

                                        Ordinal =
                                            index,

                                        DataType =
                                            inferredTypes[index],

                                        IsRequired =
                                            true,

                                        IsKey =
                                            keyNames.Contains(
                                                header.Trim(),
                                                StringComparer.OrdinalIgnoreCase)
                                    })
                            .ToList();

                    var unknownKeys =
                        keyNames
                            .Where(
                                key =>
                                    workingColumns.All(
                                        column =>
                                            !column.Name.Equals(
                                                key,
                                                StringComparison.OrdinalIgnoreCase)))
                            .ToArray();

                    if (unknownKeys.Length > 0)
                    {
                        throw new InvalidDataException(
                            $"Unknown key column(s): {string.Join(", ", unknownKeys)}.");
                    }
                }
                else
                {
                    // ====================================================
                    // EXISTING SCHEMA
                    // ====================================================

                    if (transformationConfiguration is null)
                        ValidateSchema(parsed.Headers, existingColumns);
                    else
                    {
                        var missingSources = transformationConfiguration.Mappings
                            .Where(x => !parsed.Headers.Contains(x.SourceColumn, StringComparer.OrdinalIgnoreCase))
                            .Select(x => x.SourceColumn).ToArray();
                        var unknownTargets = transformationConfiguration.Mappings
                            .Where(x => existingColumns.All(c => c.Id != x.TargetColumnId)).ToArray();
                        if (missingSources.Length > 0 || unknownTargets.Length > 0)
                            throw new InvalidDataException($"Mapping does not match staged headers/schema. Missing sources: {string.Join(", ", missingSources)}.");
                    }

                    workingColumns =
                        existingColumns;

                    var configuredKeys =
                        existingColumns
                            .Where(x => x.IsKey)
                            .Select(x => x.Name)
                            .ToArray();

                    if (keyNames.Length > 0 &&
                        !new HashSet<string>(
                            keyNames,
                            StringComparer.OrdinalIgnoreCase)
                            .SetEquals(configuredKeys))
                    {
                        throw new InvalidDataException(
                            "Import key columns do not match the dataset's configured key columns.");
                    }

                    keyNames =
                        configuredKeys;
                }

                // ========================================================
                // 12. CLEAR TRACKING
                // ========================================================

                db.ChangeTracker.Clear();

                // ========================================================
                // 13. DELETE OLD STAGING ROWS
                // ========================================================

                var oldStage =
                    db.ImportStagingRows
                        .IgnoreQueryFilters()
                        .Where(x =>
                            x.ImportId == importId);

                db.ImportStagingRows.RemoveRange(
                    oldStage);

                // ========================================================
                // 14. DELETE OLD IMPORT ERRORS
                // ========================================================

                var oldErrors =
                    db.ImportErrors
                        .IgnoreQueryFilters()
                        .Where(x =>
                            x.ImportId == importId);

                db.ImportErrors.RemoveRange(
                    oldErrors);

                await db.SaveChangesAsync(
                    token);

                // ========================================================
                // 15. BUILD STAGING DATA
                // ========================================================

                var seen =
                    new Dictionary<string, ImportStagingRow>(
                        StringComparer.Ordinal);

                var errors =
                    new List<ImportError>();

                var stages =
                    new List<ImportStagingRow>();

                foreach (var row in parsed.Rows)
                {
                    token.ThrowIfCancellationRequested();

                    var stage =
                        new ImportStagingRow
                        {
                            ImportId =
                                importId,

                            WorkspaceId =
                                import.WorkspaceId,

                            RowNumber =
                                row.RowNumber,

                            IsValid =
                                true
                        };

                    // ----------------------------------------------------
                    // Validate each value
                    // ----------------------------------------------------

                    for (var i = 0;
                         i < workingColumns.Count;
                         i++)
                    {
                        var column = workingColumns[i];
                        var mapping = transformationConfiguration?.Mappings.FirstOrDefault(x => x.TargetColumnId == column.Id);
                        var sourceIndex = mapping is null ? i : Array.FindIndex(parsed.Headers.ToArray(), x => x.Equals(mapping.SourceColumn, StringComparison.OrdinalIgnoreCase));
                        var raw = sourceIndex >= 0 && sourceIndex < row.Values.Count ? row.Values[sourceIndex] : null;
                        var processed = raw;
                        if (mapping is not null)
                        {
                            var rowData = parsed.Headers.Select((h, index) => new { h, value = index < row.Values.Count ? row.Values[index] : null })
                                .ToDictionary(x => x.h, x => x.value, StringComparer.OrdinalIgnoreCase);
                            var result = transformationEngine.Process(raw, column.DataType, mapping, rowData);
                            processed = result.Value;
                            if (!result.IsValid)
                            {
                                stage.IsValid = false;
                                if (result.TransformationError is not null)
                                {
                                    var error = MakeError(import, row.RowNumber, column.Name, raw, result.TransformationError);
                                    error.ErrorType = ImportErrorTypes.Transformation;
                                    errors.Add(error);
                                }
                                foreach (var message in result.Errors)
                                {
                                    var error = MakeError(import, row.RowNumber, column.Name, raw, message);
                                    error.ErrorType = ImportErrorTypes.Validation;
                                    error.ValidationRule = "Configured validation";
                                    errors.Add(error);
                                }
                            }
                        }

                        stage.Values.Add(
                            new ImportStagingValue
                            {
                                ColumnName =
                                    workingColumns[i].Name,

                                RawValue =
                                    processed,
                                OriginalValue = raw,
                                TransformedValue = processed
                            });

                        if (!IsCompatible(
                                processed,
                                workingColumns[i].DataType))
                        {
                            stage.IsValid =
                                false;

                            errors.Add(
                                MakeError(
                                    import,
                                    row.RowNumber,
                                    workingColumns[i].Name,
                                    processed,
                                    $"Value is not compatible with {workingColumns[i].DataType}."));
                        }
                    }

                    // ----------------------------------------------------
                    // Build key hash
                    // ----------------------------------------------------

                    stage.KeyHash =
                        BuildHash(
                            workingColumns,
                        stage.Values.Select(x => x.RawValue).ToList(),
                            keyNames);

                    // ----------------------------------------------------
                    // Duplicate inside uploaded file
                    // ----------------------------------------------------

                    if (stage.IsValid &&
                        seen.ContainsKey(stage.KeyHash))
                    {
                        if (!import.DuplicateBehavior.Equals(
                                DuplicateBehaviors.Update,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            stage.IsRejected =
                                true;

                            errors.Add(
                                MakeError(
                                    import,
                                    row.RowNumber,
                                    null,
                                    null,
                                    $"Duplicate record detected. Behavior: {import.DuplicateBehavior}."));
                        }
                    }

                    if (stage.IsValid &&
                        !stage.IsRejected)
                    {
                        seen[stage.KeyHash] =
                            stage;
                    }

                    stages.Add(stage);
                }

                // ========================================================
                // 16. SAVE STAGING
                // ========================================================

                db.ImportStagingRows.AddRange(
                    stages);

                db.ImportErrors.AddRange(
                    errors);

                await db.SaveChangesAsync(
                    token);

                // ========================================================
                // 17. SQL SERVER RETRY STRATEGY
                // ========================================================

                var strategy =
                    db.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(
                    async () =>
                    {
                        db.ChangeTracker.Clear();

                        await using var transaction =
                            await db.Database.BeginTransactionAsync(
                                token);

                        try
                        {
                            // =================================================
                            // 18. RELOAD SCHEMA INSIDE TRANSACTION
                            // =================================================

                            var transactionColumns =
                                await db.DatasetColumns
                                    .IgnoreQueryFilters()
                                    .Where(x =>
                                        x.DatasetId ==
                                        import.DatasetId)
                                    .OrderBy(x => x.Ordinal)
                                    .ToListAsync(token);

                            if (transactionColumns.Count == 0)
                            {
                                if (!isFirstSchema)
                                {
                                    throw new InvalidDataException(
                                        "Dataset schema disappeared before the final import transaction.");
                                }

                                db.DatasetColumns.AddRange(
                                    workingColumns);

                                await db.SaveChangesAsync(
                                    token);

                                transactionColumns =
                                    workingColumns;
                            }
                            else
                            {
                                if (transformationConfiguration is null)
                                    ValidateSchema(parsed.Headers, transactionColumns);

                                workingColumns =
                                    transactionColumns;
                            }

                            // =================================================
                            // 19. COLUMN MAP
                            // =================================================

                            var columnMap =
                                workingColumns.ToDictionary(
                                    x => x.Name,
                                    StringComparer.OrdinalIgnoreCase);

                            // =================================================
                            // 20. VALID STAGES
                            // =================================================

                            var validStages =
                                stages
                                    .Where(
                                        x =>
                                            x.IsValid &&
                                            !x.IsRejected)
                                    .ToList();

                            // =================================================
                            // 21. UPDATE DUPLICATES
                            // =================================================

                            if (import.DuplicateBehavior.Equals(
                                    DuplicateBehaviors.Update,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                validStages =
                                    validStages
                                        .GroupBy(
                                            x => x.KeyHash,
                                            StringComparer.Ordinal)
                                        .Select(
                                            group =>
                                                group.Last())
                                        .ToList();
                            }

                            // =================================================
                            // 22. FULL IMPORT
                            // =================================================

                            if (import.ImportMode.Equals(
                                    ImportModes.Full,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                var recordIds =
                                    await db.DatasetRecords
                                        .IgnoreQueryFilters()
                                        .Where(x =>
                                            x.DatasetId ==
                                            import.DatasetId)
                                        .Select(x => x.Id)
                                        .ToListAsync(token);

                                if (recordIds.Count > 0)
                                {
                                    await db.DatasetRecordValues
                                        .Where(
                                            x =>
                                                recordIds.Contains(
                                                    x.DatasetRecordId))
                                        .ExecuteDeleteAsync(
                                            token);

                                    await db.DatasetRecords
                                        .IgnoreQueryFilters()
                                        .Where(
                                            x =>
                                                recordIds.Contains(
                                                    x.Id))
                                        .ExecuteDeleteAsync(
                                            token);
                                }
                            }

                            // =================================================
                            // 23. PROCESS RECORDS
                            //
                            // IMPORTANT:
                            // Existing DatasetRecord entities are NOT tracked
                            // and modified through SaveChanges.
                            //
                            // This fixes the optimistic concurrency problem.
                            // =================================================

                            var success =
                                0;

                            var rejected =
                                stages.Count(
                                    x =>
                                        !x.IsValid ||
                                        x.IsRejected);

                            foreach (var stage in validStages)
                            {
                                token.ThrowIfCancellationRequested();

                                Guid recordId;

                                // =================================================
                                // APPEND
                                // =================================================

                                if (import.ImportMode.Equals(
                                        ImportModes.Append,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    var existingRecord =
                                        await db.DatasetRecords
                                            .IgnoreQueryFilters()
                                            .AsNoTracking()
                                            .FirstOrDefaultAsync(
                                                x =>
                                                    x.DatasetId ==
                                                    import.DatasetId &&
                                                    x.KeyHash ==
                                                    stage.KeyHash,
                                                token);

                                    if (existingRecord != null)
                                    {
                                        // =========================================
                                        // DUPLICATE = SKIP / REJECT
                                        // =========================================

                                        if (import.DuplicateBehavior.Equals(
                                                DuplicateBehaviors.Skip,
                                                StringComparison.OrdinalIgnoreCase) ||
                                            import.DuplicateBehavior.Equals(
                                                DuplicateBehaviors.Reject,
                                                StringComparison.OrdinalIgnoreCase))
                                        {
                                            rejected++;

                                            db.ImportErrors.Add(
                                                MakeError(
                                                    import,
                                                    stage.RowNumber,
                                                    null,
                                                    null,
                                                    $"Duplicate record already exists in the dataset. Behavior: {import.DuplicateBehavior}."));

                                            continue;
                                        }

                                        // =========================================
                                        // DUPLICATE = UPDATE
                                        //
                                        // DO NOT TRACK THE ENTITY.
                                        //
                                        // ExecuteUpdate bypasses the stale
                                        // concurrency-tracked entity problem.
                                        // =========================================

                                        recordId =
                                            existingRecord.Id;

                                        var updatedRows =
                                            await db.DatasetRecords
                                                .IgnoreQueryFilters()
                                                .Where(
                                                    x =>
                                                        x.Id ==
                                                        recordId &&
                                                        x.DatasetId ==
                                                        import.DatasetId)
                                                .ExecuteUpdateAsync(
                                                    setters =>
                                                        setters
                                                            .SetProperty(
                                                                x =>
                                                                    x.SourceImportId,
                                                                importId)

                                                            .SetProperty(
                                                                x =>
                                                                    x.UpdatedAtUtc,
                                                                DateTime.UtcNow),
                                                    token);

                                        if (updatedRows != 1)
                                        {
                                            throw new DbUpdateConcurrencyException(
                                                $"Existing dataset record {recordId} could not be updated.");
                                        }

                                        // =========================================
                                        // Remove old values
                                        // =========================================

                                        await db.DatasetRecordValues
                                            .Where(
                                                x =>
                                                    x.DatasetRecordId ==
                                                    recordId)
                                            .ExecuteDeleteAsync(
                                                token);
                                    }
                                    else
                                    {
                                        // =========================================
                                        // NEW RECORD
                                        // =========================================

                                        var newRecord =
                                            new DatasetRecord
                                            {
                                                DatasetId =
                                                    import.DatasetId,

                                                WorkspaceId =
                                                    import.WorkspaceId,

                                                SourceImportId =
                                                    importId,

                                                KeyHash =
                                                    stage.KeyHash
                                            };

                                        db.DatasetRecords.Add(
                                            newRecord);

                                        // Save first so that database-generated
                                        // record IDs are available.
                                        await db.SaveChangesAsync(
                                            token);

                                        recordId =
                                            newRecord.Id;
                                    }
                                }
                                else
                                {
                                    // =================================================
                                    // FULL IMPORT
                                    //
                                    // All existing records were already deleted.
                                    // =================================================

                                    var newRecord =
                                        new DatasetRecord
                                        {
                                            DatasetId =
                                                import.DatasetId,

                                            WorkspaceId =
                                                import.WorkspaceId,

                                            SourceImportId =
                                                importId,

                                            KeyHash =
                                                stage.KeyHash
                                        };

                                    db.DatasetRecords.Add(
                                        newRecord);

                                    await db.SaveChangesAsync(
                                        token);

                                    recordId =
                                        newRecord.Id;
                                }

                                // =================================================
                                // 24. INSERT RECORD VALUES
                                //
                                // Do NOT use record.Values.Add().
                                // Insert values explicitly using recordId.
                                // =================================================

                                foreach (var stagingValue
                                    in stage.Values)
                                {
                                    if (!columnMap.TryGetValue(
                                            stagingValue.ColumnName,
                                            out var column))
                                    {
                                        throw new InvalidDataException(
                                            $"Column '{stagingValue.ColumnName}' was not found in the dataset schema.");
                                    }

                                    var recordValue =
                                        ToRecordValue(
                                            recordId,
                                            column,
                                            stagingValue.RawValue);

                                    db.DatasetRecordValues.Add(
                                        recordValue);
                                }

                                await db.SaveChangesAsync(
                                    token);

                                success++;
                            }

                            // =================================================
                            // 25. COUNT IMPORT ERRORS
                            // =================================================

                            var errorCount =
                                await db.ImportErrors
                                    .IgnoreQueryFilters()
                                    .CountAsync(
                                        x =>
                                            x.ImportId ==
                                            importId,
                                        token);

                            // =================================================
                            // 26. FINAL STATUS
                            // =================================================

                            var finalStatus =
                                ImportRules.CompletionStatus(
                                    errorCount,
                                    rejected);

                            // =================================================
                            // 27. UPDATE IMPORT
                            //
                            // ExecuteUpdate avoids DataImport concurrency
                            // tracking problems as well.
                            // =================================================

                            var updatedImport =
                                await db.DataImports
                                    .IgnoreQueryFilters()
                                    .Where(
                                        x =>
                                            x.Id ==
                                            importId &&
                                            x.Status ==
                                            ImportStatuses.Processing)
                                    .ExecuteUpdateAsync(
                                        setters =>
                                            setters
                                                .SetProperty(
                                                    x =>
                                                        x.SuccessfullyImportedRecords,
                                                    success)

                                                .SetProperty(
                                                    x =>
                                                        x.RejectedRecords,
                                                    rejected)

                                                .SetProperty(
                                                    x =>
                                                        x.ErrorCount,
                                                    errorCount)

                                                .SetProperty(
                                                    x =>
                                                        x.Status,
                                                    finalStatus)

                                                .SetProperty(
                                                    x =>
                                                        x.CompletedAtUtc,
                                                    DateTime.UtcNow),
                                        token);

                            if (updatedImport != 1)
                            {
                                throw new DbUpdateConcurrencyException(
                                    $"Import {importId} could not be completed because its status changed before finalization.");
                            }

                            // =================================================
                            // 28. COMMIT
                            // =================================================

                            await transaction.CommitAsync(
                                token);
                        }
                        catch
                        {
                            await transaction.RollbackAsync(
                                CancellationToken.None);

                            throw;
                        }
                    });

                // ========================================================
                // 29. READ FINAL IMPORT STATE
                // ========================================================

                var completed =
                    await db.DataImports
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            x =>
                                x.Id ==
                                importId,
                            hostToken);

                if (completed != null)
                {
                    await audit.WriteAsync(
                        "Import Completion",
                        "DataImport",
                        importId.ToString(),
                        $"Status={completed.Status}; " +
                        $"Total={completed.TotalRecords}; " +
                        $"Success={completed.SuccessfullyImportedRecords}; " +
                        $"Rejected={completed.RejectedRecords}",
                        userId:
                            completed.InitiatedByUserId,
                        workspaceId:
                            completed.WorkspaceId,
                        cancellationToken:
                            hostToken);
                }
            }
            finally
            {
                cancellations.Unregister(
                    importId);
            }
        }
        catch (OperationCanceledException)
        {
            await MarkCancelledAsync(
                importId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Import {ImportId} failed.",
                importId);

            await MarkFailedAsync(
                importId,
                ex);
        }
    }

    // ====================================================================
    // MARK CANCELLED
    // ====================================================================

    private async Task MarkCancelledAsync(
        Guid importId)
    {
        try
        {
            db.ChangeTracker.Clear();

            await db.DataImports
                .IgnoreQueryFilters()
                .Where(
                    x =>
                        x.Id == importId &&
                        (
                            x.Status ==
                            ImportStatuses.Queued ||

                            x.Status ==
                            ImportStatuses.Processing
                        ))
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                x =>
                                    x.Status,
                                ImportStatuses.Cancelled)

                            .SetProperty(
                                x =>
                                    x.CancellationRequested,
                                true)

                            .SetProperty(
                                x =>
                                    x.CompletedAtUtc,
                                DateTime.UtcNow),
                    CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unable to mark import {ImportId} as Cancelled.",
                importId);
        }
    }

    // ====================================================================
    // MARK FAILED
    // ====================================================================

    private async Task MarkFailedAsync(
        Guid importId,
        Exception ex)
    {
        try
        {
            db.ChangeTracker.Clear();

            var failureMessage =
                BuildFailureMessage(ex);

            var safeFailureMessage =
                failureMessage.Length > 4000
                    ? failureMessage.Substring(
                        0,
                        4000)
                    : failureMessage;

            await db.DataImports
                .IgnoreQueryFilters()
                .Where(
                    x =>
                        x.Id == importId &&

                        x.Status !=
                        ImportStatuses.Completed &&

                        x.Status !=
                        ImportStatuses.CompletedWithErrors &&

                        x.Status !=
                        ImportStatuses.Cancelled)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                x =>
                                    x.Status,
                                ImportStatuses.Failed)

                            .SetProperty(
                                x =>
                                    x.CompletedAtUtc,
                                DateTime.UtcNow)

                            .SetProperty(
                                x =>
                                    x.FailureMessage,
                                safeFailureMessage),
                    CancellationToken.None);

            var failed =
                await db.DataImports
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id ==
                            importId,
                        CancellationToken.None);

            if (failed == null)
            {
                return;
            }

            db.ImportErrors.Add(
                MakeError(
                    failed,
                    null,
                    null,
                    null,
                    safeFailureMessage));

            await db.SaveChangesAsync(
                CancellationToken.None);

            await audit.WriteAsync(
                "Import Failure",
                "DataImport",
                importId.ToString(),
                safeFailureMessage,
                userId:
                    failed.InitiatedByUserId,
                workspaceId:
                    failed.WorkspaceId,
                cancellationToken:
                    CancellationToken.None);
        }
        catch (Exception failureUpdateException)
        {
            logger.LogError(
                failureUpdateException,
                "Unable to mark import {ImportId} as Failed.",
                importId);
        }
    }

    // ====================================================================
    // BUILD FAILURE MESSAGE
    // ====================================================================

    private static string BuildFailureMessage(
        Exception ex)
    {
        var messages =
            new List<string>();

        Exception? current =
            ex;

        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(
                    current.Message))
            {
                messages.Add(
                    current.Message);
            }

            current =
                current.InnerException;
        }

        return string.Join(
            " Inner: ",
            messages.Distinct());
    }

    // ====================================================================
    // PARSE KEY COLUMNS
    // ====================================================================

    private static string[] ParseKeyColumns(
        string? keyColumnsJson)
    {
        if (string.IsNullOrWhiteSpace(
                keyColumnsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(
                       keyColumnsJson)
                   ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            throw new InvalidDataException(
                "KeyColumnsJson contains invalid JSON.");
        }
    }

    // ====================================================================
    // VALIDATE HEADERS
    // ====================================================================

    private static void ValidateHeaders(
        IReadOnlyList<string> headers)
    {
        if (headers.Count == 0)
        {
            throw new InvalidDataException(
                "File contains no headers.");
        }

        if (headers.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                "File contains missing or blank headers.");
        }

        var duplicate =
            headers
                .GroupBy(
                    x =>
                        x.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(
                    g =>
                        g.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidDataException(
                $"Duplicate column name '{duplicate.Key}' was found.");
        }
    }

    // ====================================================================
    // VALIDATE SCHEMA
    // ====================================================================

    private static void ValidateSchema(
        IReadOnlyList<string> headers,
        IReadOnlyList<DatasetColumn> columns)
    {
        var incoming =
            new HashSet<string>(
                headers.Select(
                    x =>
                        x.Trim()),
                StringComparer.OrdinalIgnoreCase);

        var expected =
            new HashSet<string>(
                columns.Select(
                    x =>
                        x.Name),
                StringComparer.OrdinalIgnoreCase);

        if (incoming.SetEquals(
                expected))
        {
            return;
        }

        var missing =
            expected.Except(
                incoming,
                StringComparer.OrdinalIgnoreCase);

        var extra =
            incoming.Except(
                expected,
                StringComparer.OrdinalIgnoreCase);

        throw new InvalidDataException(
            $"Schema mismatch. " +
            $"Missing: [{string.Join(", ", missing)}]. " +
            $"Additional: [{string.Join(", ", extra)}].");
    }

    // ====================================================================
    // INFER TYPES
    // ====================================================================

    private static string[] InferTypes(
        ParsedImportFile file)
    {
        var result =
            new string[file.Headers.Count];

        for (var i = 0;
             i < result.Length;
             i++)
        {
            var values =
                file.Rows
                    .Select(
                        row =>
                            i < row.Values.Count
                                ? row.Values[i]
                                : null)
                    .Where(
                        value =>
                            !string.IsNullOrWhiteSpace(
                                value))
                    .ToArray();

            if (values.Length == 0)
            {
                result[i] =
                    DatasetColumnTypes.String;
            }
            else if (values.All(
                         value =>
                             long.TryParse(
                                 value,
                                 NumberStyles.Integer,
                                 CultureInfo.InvariantCulture,
                                 out _)))
            {
                result[i] =
                    DatasetColumnTypes.Integer;
            }
            else if (values.All(
                         value =>
                             decimal.TryParse(
                                 value,
                                 NumberStyles.Number,
                                 CultureInfo.InvariantCulture,
                                 out _)))
            {
                result[i] =
                    DatasetColumnTypes.Decimal;
            }
            else if (values.All(
                         IsBoolean))
            {
                result[i] =
                    DatasetColumnTypes.Boolean;
            }
            else if (values.All(
                         value =>
                             DateTime.TryParse(
                                 value,
                                 CultureInfo.InvariantCulture,
                                 DateTimeStyles.AllowWhiteSpaces,
                                 out _)))
            {
                result[i] =
                    DatasetColumnTypes.DateTime;
            }
            else
            {
                result[i] =
                    DatasetColumnTypes.String;
            }
        }

        return result;
    }

    // ====================================================================
    // TYPE VALIDATION
    // ====================================================================

    private static bool IsCompatible(
        string? value,
        string type)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return true;
        }

        return type switch
        {
            DatasetColumnTypes.Integer =>
                long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out _),

            DatasetColumnTypes.Decimal =>
                decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out _),

            DatasetColumnTypes.Boolean =>
                IsBoolean(value),

            DatasetColumnTypes.DateTime =>
                DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out _),

            _ => true
        };
    }

    // ====================================================================
    // BOOLEAN
    // ====================================================================

    private static bool IsBoolean(
        string? value)
    {
        if (bool.TryParse(
                value,
                out _))
        {
            return true;
        }

        return value is "0" or "1";
    }

    // ====================================================================
    // BUILD KEY HASH
    // ====================================================================

    private static string BuildHash(
        IReadOnlyList<DatasetColumn> columns,
        IReadOnlyList<string?> values,
        IReadOnlyList<string> keys)
    {
        IEnumerable<int> indexes;

        if (keys.Count > 0)
        {
            indexes =
                columns
                    .Select(
                        (column, index) =>
                            (column, index))
                    .Where(
                        x =>
                            keys.Contains(
                                x.column.Name,
                                StringComparer.OrdinalIgnoreCase))
                    .Select(
                        x =>
                            x.index);
        }
        else
        {
            indexes =
                Enumerable.Range(
                    0,
                    columns.Count);
        }

        var raw =
            string.Join(
                "\u001f",
                indexes.Select(
                    index =>
                        (
                            index < values.Count
                                ? values[index]
                                : null
                        )?.Trim() ??
                        string.Empty));

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    raw)));
    }

    // ====================================================================
    // CREATE IMPORT ERROR
    // ====================================================================

    private static ImportError MakeError(
        DataImport import,
        int? row,
        string? column,
        string? value,
        string message)
    {
        return new ImportError
        {
            ImportId =
                import.Id,

            WorkspaceId =
                import.WorkspaceId,

            RowNumber =
                row,

            ColumnName =
                column,

            InvalidValue =
                value,

            ErrorDescription =
                message
        };
    }

    // ====================================================================
    // CONVERT RECORD VALUE
    // ====================================================================

    private static DatasetRecordValue ToRecordValue(
        Guid recordId,
        DatasetColumn column,
        string? raw)
    {
        var value =
            new DatasetRecordValue
            {
                DatasetRecordId =
                    recordId,

                DatasetColumnId =
                    column.Id,

                RawValue =
                    raw
            };

        if (string.IsNullOrWhiteSpace(
                raw))
        {
            return value;
        }

        switch (column.DataType)
        {
            case DatasetColumnTypes.Integer:

                if (long.TryParse(
                        raw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var integerValue))
                {
                    value.IntegerValue =
                        integerValue;
                }

                break;

            case DatasetColumnTypes.Decimal:

                if (decimal.TryParse(
                        raw,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var decimalValue))
                {
                    value.DecimalValue =
                        decimalValue;
                }

                break;

            case DatasetColumnTypes.Boolean:

                value.BooleanValue =
                    raw == "1"
                        ? true
                        : raw == "0"
                            ? false
                            : bool.Parse(raw);

                break;

            case DatasetColumnTypes.DateTime:

                if (DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var dateTimeValue))
                {
                    value.DateTimeValue =
                        dateTimeValue;
                }

                break;

            default:

                value.StringValue =
                    raw;

                break;
        }

        return value;
    }
}
