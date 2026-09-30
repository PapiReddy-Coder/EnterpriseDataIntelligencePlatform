using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

/// <summary>Idempotently records lineage for activities created after Task 21 deployment.</summary>
public sealed class LineageCaptureService(AppDbContext db) : ILineageCaptureService
{
    public async Task CaptureDatasetVersionAsync(Guid datasetVersionId, CancellationToken ct)
    {
        var context = await VersionContextAsync(datasetVersionId, ct);
        await EnsureVersionEdgesAsync(context, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CaptureTransformationAsync(Guid transformationId, CancellationToken ct)
    {
        var transformation = await db.DatasetTransformationConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == transformationId, ct);
        var context = await ActivityContextAsync(transformation.DatasetId, transformation.CreatedAtUtc, ct);
        await EnsureVersionEdgesAsync(context, ct);
        await AddAsync(context, Node.DatasetVersion(context), new(
            LineageEntityTypes.Transformation, transformation.Id, transformation.DatasetId,
            $"Transformation configuration v{transformation.Version}", transformation.Version),
            LineageRelationshipTypes.Configures, LineageEntityTypes.Transformation, transformation.Id,
            transformation.CreatedByUserId, JsonSerializer.Serialize(new { transformationConfigurationVersion = transformation.Version }), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CaptureImportAsync(Guid importId, CancellationToken ct)
    {
        var import = await db.DataImports.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.File).SingleAsync(x => x.Id == importId, ct);
        var context = await ActivityContextAsync(import.DatasetId, import.CreatedAtUtc, ct);
        await EnsureVersionEdgesAsync(context, ct);

        var importNode = new Node(LineageEntityTypes.Import, import.Id, import.DatasetId,
            $"Import {import.Id:N}", null);
        var fileNode = new Node(LineageEntityTypes.SourceFile, import.File.Id, import.DatasetId,
            import.File.OriginalFileName, null);
        await AddAsync(context, fileNode, importNode, LineageRelationshipTypes.ProvidesData,
            LineageEntityTypes.Import, import.Id, import.InitiatedByUserId,
            JsonSerializer.Serialize(new { import.File.Extension, import.File.FileSizeBytes }), ct);
        await AddAsync(context, importNode, Node.DatasetVersion(context), LineageRelationshipTypes.Loads,
            LineageEntityTypes.Import, import.Id, import.InitiatedByUserId,
            JsonSerializer.Serialize(new { import.ImportMode, import.DuplicateBehavior }), ct);

        if (import.TransformationConfigurationId.HasValue)
        {
            var configuration = await db.DatasetTransformationConfigurations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == import.TransformationConfigurationId.Value, ct);
            if (configuration is not null)
                await AddAsync(context,
                    new(LineageEntityTypes.Transformation, configuration.Id, configuration.DatasetId,
                        $"Transformation configuration v{configuration.Version}", configuration.Version),
                    importNode, LineageRelationshipTypes.AppliesTransformation,
                    LineageEntityTypes.Import, import.Id, import.InitiatedByUserId,
                    JsonSerializer.Serialize(new { transformationConfigurationVersion = configuration.Version }), ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task CaptureQualityProfileAsync(Guid profileRunId, CancellationToken ct)
    {
        var run = await db.DataQualityProfileRuns.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == profileRunId, ct);
        var context = await ActivityContextAsync(run.DatasetId, run.CreatedAtUtc, ct);
        await EnsureVersionEdgesAsync(context, ct);
        await AddAsync(context, Node.DatasetVersion(context),
            new(LineageEntityTypes.QualityProfile, run.Id, run.DatasetId, $"Quality profile {run.Id:N}", null),
            LineageRelationshipTypes.Profiles, LineageEntityTypes.QualityProfile, run.Id,
            run.RequestedByUserId, JsonSerializer.Serialize(new { run.TriggerType, run.ImportId }), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureVersionEdgesAsync(ActivityContext context, CancellationToken ct)
    {
        var dataset = new Node(LineageEntityTypes.Dataset, context.Dataset.Id, context.Dataset.Id,
            $"{context.Dataset.Code} - {context.Dataset.Name}", context.Version.VersionNumber);
        var version = Node.DatasetVersion(context);
        await AddAsync(context, dataset, version, LineageRelationshipTypes.VersionOf,
            LineageEntityTypes.DatasetVersion, context.Version.Id, context.Version.CreatedByUserId,
            JsonSerializer.Serialize(new { context.Version.VersionNumber, context.Version.VersionNotes }), ct);

        var sourceId = StableSourceId(context.Dataset.WorkspaceId, context.Version.DataSourceType, context.Version.DataSourceName);
        var source = new Node(LineageEntityTypes.DataSource, sourceId, context.Dataset.Id,
            $"{context.Version.DataSourceType}: {context.Version.DataSourceName}", null);
        await AddAsync(context, source, version, LineageRelationshipTypes.SourcedFrom,
            LineageEntityTypes.DatasetVersion, context.Version.Id, context.Version.CreatedByUserId,
            JsonSerializer.Serialize(new
            {
                dataSourceName = context.Version.DataSourceName,
                dataSourceType = context.Version.DataSourceType,
                description = context.Version.DataSourceDescription
            }), ct);
    }

    private async Task AddAsync(ActivityContext context, Node source, Node target, string relationshipType,
        string? processType, Guid? processId, Guid? createdBy, string metadataJson, CancellationToken ct)
    {
        var tracked = db.ChangeTracker.Entries<LineageRelationship>().Any(x => x.State == EntityState.Added &&
            x.Entity.SourceEntityType == source.Type && x.Entity.SourceEntityId == source.Id &&
            x.Entity.TargetEntityType == target.Type && x.Entity.TargetEntityId == target.Id &&
            x.Entity.RelationshipType == relationshipType && x.Entity.IsActive);
        if (tracked || await db.LineageRelationships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.SourceEntityType == source.Type && x.SourceEntityId == source.Id &&
                x.TargetEntityType == target.Type && x.TargetEntityId == target.Id &&
                x.RelationshipType == relationshipType && x.IsActive, ct)) return;

        db.LineageRelationships.Add(new LineageRelationship
        {
            WorkspaceId = context.Dataset.WorkspaceId,
            DatasetId = target.DatasetId ?? context.Dataset.Id,
            OwnerId = context.Dataset.OwnerId,
            SourceEntityType = source.Type,
            SourceEntityId = source.Id,
            SourceDatasetId = source.DatasetId,
            SourceDisplayName = source.DisplayName,
            SourceVersionNumber = source.Version,
            TargetEntityType = target.Type,
            TargetEntityId = target.Id,
            TargetDatasetId = target.DatasetId,
            TargetDisplayName = target.DisplayName,
            TargetVersionNumber = target.Version,
            RelationshipType = relationshipType,
            RelevantDatasetVersionId = context.Version.Id,
            RelevantDatasetVersionNumber = context.Version.VersionNumber,
            ProcessEntityType = processType,
            ProcessEntityId = processId,
            MetadataJson = metadataJson,
            IsAutomatic = true,
            IsActive = true,
            CreatedByUserId = createdBy,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task<ActivityContext> VersionContextAsync(Guid datasetVersionId, CancellationToken ct)
    {
        var version = await db.DatasetVersions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == datasetVersionId, ct);
        var dataset = await db.Datasets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == version.DatasetId, ct);
        return new(dataset, version);
    }

    private async Task<ActivityContext> ActivityContextAsync(Guid datasetId, DateTime occurredAtUtc, CancellationToken ct)
    {
        var dataset = await db.Datasets.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == datasetId, ct);
        var version = await db.DatasetVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DatasetId == datasetId && x.CreatedAtUtc <= occurredAtUtc)
            .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct)
            ?? await db.DatasetVersions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.DatasetId == datasetId).OrderByDescending(x => x.VersionNumber).FirstAsync(ct);
        return new(dataset, version);
    }

    internal static Guid StableSourceId(Guid workspaceId, string sourceType, string sourceName)
    {
        var value = $"{workspaceId:N}|{sourceType.Trim().ToUpperInvariant()}|{sourceName.Trim().ToUpperInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record ActivityContext(Dataset Dataset, DatasetVersion Version);
    private sealed record Node(string Type, Guid Id, Guid? DatasetId, string DisplayName, int? Version)
    {
        public static Node DatasetVersion(ActivityContext context) => new(LineageEntityTypes.DatasetVersion,
            context.Version.Id, context.Dataset.Id,
            $"{context.Dataset.Code} v{context.Version.VersionNumber}", context.Version.VersionNumber);
    }
}
