using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class TransformationService(AppDbContext db, ICurrentUser currentUser, ITransformationEngine engine, IAuditService audit)
    : ITransformationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceResult<TransformationConfigurationResponse>> SaveAsync(Guid datasetId, SaveTransformationConfigurationRequest request, CancellationToken ct)
    {
        var dataset = await db.Datasets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == datasetId, ct);
        if (dataset is null) return ServiceResult<TransformationConfigurationResponse>.Failure("Dataset not found or not accessible.", 404);
        if (!currentUser.UserId.HasValue) return ServiceResult<TransformationConfigurationResponse>.Failure("Authenticated user context is required.", 401);
        var columns = await db.DatasetColumns.Where(x => x.DatasetId == datasetId).ToListAsync(ct);
        var columnIds = columns.Select(x => x.Id).ToHashSet();
        if (request.Mappings.Count == 0 || request.Mappings.Any(x => string.IsNullOrWhiteSpace(x.SourceColumn) || !columnIds.Contains(x.TargetColumnId)))
            return ServiceResult<TransformationConfigurationResponse>.Failure("Mappings must reference existing dataset columns.", 400);
        if (request.Mappings.Select(x => x.SourceColumn.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Mappings.Count)
            return ServiceResult<TransformationConfigurationResponse>.Failure("Source columns must be unique.", 400);
        if (request.Mappings.SelectMany(x => x.Transformations).Any(x => !TransformationTypes.All.Contains(x.Type, StringComparer.OrdinalIgnoreCase)))
            return ServiceResult<TransformationConfigurationResponse>.Failure("One or more transformation types are unsupported.", 400);

        var version = (await db.DatasetTransformationConfigurations.Where(x => x.DatasetId == datasetId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        await db.DatasetTransformationConfigurations.Where(x => x.DatasetId == datasetId && x.IsActive)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.IsActive, false), ct);
        var entity = new DatasetTransformationConfiguration
        {
            DatasetId = datasetId,
            WorkspaceId = dataset.WorkspaceId,
            Version = version,
            IsActive = true,
            ConfigurationJson = JsonSerializer.Serialize(request, JsonOptions),
            CreatedByUserId = currentUser.UserId.Value
        };
        db.DatasetTransformationConfigurations.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(version == 1 ? "Mapping Creation" : "Transformation Configuration Changes", "DatasetTransformationConfiguration",
            entity.Id.ToString(),
            $"Saved dataset {datasetId} transformation configuration version {version}.",
            userId: currentUser.UserId,
            workspaceId: dataset.WorkspaceId,
            cancellationToken: ct);
        return ServiceResult<TransformationConfigurationResponse>.Success(ToResponse(entity), 201);
    }

    public async Task<ServiceResult<TransformationConfigurationResponse>> GetActiveAsync(Guid datasetId, CancellationToken ct)
    {
        var entity = await db.DatasetTransformationConfigurations.AsNoTracking().FirstOrDefaultAsync(x => x.DatasetId == datasetId && x.IsActive, ct);
        return entity is null ? ServiceResult<TransformationConfigurationResponse>.Failure("No active configuration was found.", 404)
            : ServiceResult<TransformationConfigurationResponse>.Success(ToResponse(entity));
    }

    public async Task<ServiceResult<IReadOnlyList<TransformationConfigurationResponse>>> HistoryAsync(Guid datasetId, CancellationToken ct)
    {
        if (!await db.Datasets.AnyAsync(x => x.Id == datasetId, ct)) return ServiceResult<IReadOnlyList<TransformationConfigurationResponse>>.Failure("Dataset not found or not accessible.", 404);
        var items = await db.DatasetTransformationConfigurations.AsNoTracking().Where(x => x.DatasetId == datasetId).OrderByDescending(x => x.Version).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<TransformationConfigurationResponse>>.Success(items.Select(ToResponse).ToList());
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid datasetId, Guid configurationId, CancellationToken ct)
    {
        var entity = await db.DatasetTransformationConfigurations.FirstOrDefaultAsync(x => x.Id == configurationId && x.DatasetId == datasetId, ct);
        if (entity is null) return ServiceResult<bool>.Failure("Configuration not found.", 404);
        if (await db.DataImports.AnyAsync(x => x.TransformationConfigurationId == configurationId, ct))
            return ServiceResult<bool>.Failure("A configuration used by an import is immutable and cannot be deleted.", 409);
        db.Remove(entity); await db.SaveChangesAsync(ct);
        await audit.WriteAsync(
            "Mapping Deletion",
            "DatasetTransformationConfiguration",
            entity.Id.ToString(),
            $"Deleted unused version {entity.Version}.",
            userId: currentUser.UserId,
            workspaceId: entity.WorkspaceId,
            cancellationToken: ct);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<TransformationPreviewResponse>> PreviewAsync(Guid importId, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 100) return ServiceResult<TransformationPreviewResponse>.Failure("Preview limit must be between 1 and 100.", 400);
        var import = await db.DataImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == importId, ct);
        if (import is null) return ServiceResult<TransformationPreviewResponse>.Failure("Import not found or not accessible.", 404);
        var config = import.TransformationConfigurationId.HasValue
            ? await db.DatasetTransformationConfigurations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == import.TransformationConfigurationId, ct)
            : await db.DatasetTransformationConfigurations.AsNoTracking().FirstOrDefaultAsync(x => x.DatasetId == import.DatasetId && x.IsActive, ct);
        if (config is null) return ServiceResult<TransformationPreviewResponse>.Failure("No transformation configuration is available.", 409);
        var columns = await db.DatasetColumns.Where(x => x.DatasetId == import.DatasetId).ToDictionaryAsync(x => x.Id, ct);
        var stages = await db.ImportStagingRows.AsNoTracking().Include(x => x.Values).Where(x => x.ImportId == importId).OrderBy(x => x.RowNumber).Take(limit).ToListAsync(ct);
        var mappings = Deserialize(config).Mappings;
        var rows = stages.Select(stage =>
        {
            var raw = stage.Values.ToDictionary(x => x.ColumnName, x => x.OriginalValue ?? x.RawValue, StringComparer.OrdinalIgnoreCase);
            var values = mappings.Select(mapping =>
            {
                var staged = stage.Values.FirstOrDefault(x => x.ColumnName.Equals(columns[mapping.TargetColumnId].Name, StringComparison.OrdinalIgnoreCase));
                var original = staged?.OriginalValue;
                var target = columns[mapping.TargetColumnId];
                var result = engine.Process(original, target.DataType, mapping, raw);
                IReadOnlyList<string> previewErrors = result.TransformationError is null
                    ? result.Errors
                    : new[] { result.TransformationError };
                return new TransformationPreviewValue(mapping.SourceColumn, target.Name, original, result.Value, result.IsValid, previewErrors);
            }).ToList();
            return new TransformationPreviewRow(stage.RowNumber, values.All(x => x.IsValid), values);
        }).ToList();
        return ServiceResult<TransformationPreviewResponse>.Success(new(importId, config.Id, config.Version, rows));
    }

    private static SaveTransformationConfigurationRequest Deserialize(
        DatasetTransformationConfiguration entity) =>
        JsonSerializer.Deserialize<SaveTransformationConfigurationRequest>(
            entity.ConfigurationJson,
            JsonOptions)
        ?? new SaveTransformationConfigurationRequest
        {
            Mappings = []
        };
    private static TransformationConfigurationResponse ToResponse(DatasetTransformationConfiguration entity) =>
        new(entity.Id, entity.DatasetId, entity.Version, entity.IsActive, Deserialize(entity).Mappings, entity.CreatedByUserId, entity.CreatedAtUtc);
}
