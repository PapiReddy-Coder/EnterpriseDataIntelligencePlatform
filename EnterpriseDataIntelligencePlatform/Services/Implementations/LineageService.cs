using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class LineageOptions
{
    public int DefaultTraversalDepth { get; set; } = 5;
    public int MaxTraversalDepth { get; set; } = 10;
    public int MaxGraphNodes { get; set; } = 1000;
}

public sealed class LineageService(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    IOptions<LineageOptions> options,
    IDatasetAccessPolicy? datasetAccess = null) : ILineageService
{
    private readonly LineageOptions _options = options.Value;

    public async Task<AnalyticsPage<LineageRelationshipResponse>> SearchAsync(LineageSearchQuery query, CancellationToken ct)
    {
        await ValidateSearchAsync(query, ct);
        if (query.DatasetId.HasValue) await EnsureDatasetAccessAsync(query.DatasetId.Value, DatasetAccessLevels.Read, ct);
        var sourceType = NormalizeOptional(query.SourceType, LineageEntityTypes.All, "SourceType");
        var targetType = NormalizeOptional(query.TargetType, LineageEntityTypes.All, "TargetType");
        var relationshipType = NormalizeOptional(query.RelationshipType, LineageRelationshipTypes.All, "RelationshipType");

        var rows = db.LineageRelationships.AsNoTracking().Where(x => db.Datasets.Any(d => d.Id == x.DatasetId));
        if (!currentUser.IsPlatformAdministrator && currentUser.WorkspaceId.HasValue)
            rows = rows.Where(x => x.WorkspaceId == currentUser.WorkspaceId.Value);
        if (query.WorkspaceId.HasValue) rows = rows.Where(x => x.WorkspaceId == query.WorkspaceId);
        if (query.DatasetId.HasValue)
            rows = rows.Where(x => x.DatasetId == query.DatasetId || x.SourceDatasetId == query.DatasetId || x.TargetDatasetId == query.DatasetId);
        if (sourceType is not null) rows = rows.Where(x => x.SourceEntityType == sourceType);
        if (targetType is not null) rows = rows.Where(x => x.TargetEntityType == targetType);
        if (relationshipType is not null) rows = rows.Where(x => x.RelationshipType == relationshipType);
        if (query.DatasetVersion.HasValue)
            rows = rows.Where(x => x.RelevantDatasetVersionNumber == query.DatasetVersion ||
                                   x.SourceVersionNumber == query.DatasetVersion || x.TargetVersionNumber == query.DatasetVersion);
        if (query.ImportId.HasValue)
            rows = rows.Where(x =>
                (x.SourceEntityType == LineageEntityTypes.Import && x.SourceEntityId == query.ImportId) ||
                (x.TargetEntityType == LineageEntityTypes.Import && x.TargetEntityId == query.ImportId) ||
                (x.ProcessEntityType == LineageEntityTypes.Import && x.ProcessEntityId == query.ImportId));
        if (query.CreatedByUserId.HasValue) rows = rows.Where(x => x.CreatedByUserId == query.CreatedByUserId);
        if (query.OwnerId.HasValue) rows = rows.Where(x => x.OwnerId == query.OwnerId);
        if (query.FromUtc.HasValue) rows = rows.Where(x => x.CreatedAtUtc >= query.FromUtc);
        if (query.ToUtc.HasValue) rows = rows.Where(x => x.CreatedAtUtc < query.ToUtc);
        if (query.IsActive.HasValue) rows = rows.Where(x => x.IsActive == query.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.SourceDisplayName.Contains(search) || x.TargetDisplayName.Contains(search));
        }

        var ascending = query.SortDirection == "asc";
        var sorted = (query.SortBy ?? "createdAtUtc") switch
        {
            "relationshipType" => ascending ? rows.OrderBy(x => x.RelationshipType) : rows.OrderByDescending(x => x.RelationshipType),
            "sourceType" => ascending ? rows.OrderBy(x => x.SourceEntityType) : rows.OrderByDescending(x => x.SourceEntityType),
            "targetType" => ascending ? rows.OrderBy(x => x.TargetEntityType) : rows.OrderByDescending(x => x.TargetEntityType),
            "sourceName" => ascending ? rows.OrderBy(x => x.SourceDisplayName) : rows.OrderByDescending(x => x.SourceDisplayName),
            "targetName" => ascending ? rows.OrderBy(x => x.TargetDisplayName) : rows.OrderByDescending(x => x.TargetDisplayName),
            _ => ascending ? rows.OrderBy(x => x.CreatedAtUtc) : rows.OrderByDescending(x => x.CreatedAtUtc)
        };
        sorted = sorted.ThenBy(x => x.Id);

        var total = await rows.LongCountAsync(ct);
        var entities = await sorted.Skip(checked((query.Page - 1) * query.PageSize)).Take(query.PageSize).ToListAsync(ct);
        return new(entities.Select(ToRelationshipResponse).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<LineageRelationshipResponse> GetRelationshipAsync(Guid relationshipId, CancellationToken ct)
    {
        NotEmpty(relationshipId, "RelationshipId");
        var entity = await db.LineageRelationships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == relationshipId, ct)
            ?? throw NotFound("Lineage relationship was not found or is not accessible.");
        await EnsureDatasetAccessAsync(entity.DatasetId, DatasetAccessLevels.Read, ct);
        if (!CanAccessWorkspace(entity.WorkspaceId)) throw NotFound("Lineage relationship was not found or is not accessible.");
        return ToRelationshipResponse(entity);
    }

    public async Task<LineageRelationshipResponse> CreateAsync(CreateLineageRelationshipRequest request, CancellationToken ct)
    {
        RequireUser();
        NotEmpty(request.SourceEntityId, "SourceEntityId");
        NotEmpty(request.TargetEntityId, "TargetEntityId");
        var sourceType = Normalize(request.SourceEntityType, LineageEntityTypes.Manual, "SourceEntityType");
        var targetType = Normalize(request.TargetEntityType, LineageEntityTypes.Manual, "TargetEntityType");
        var relationshipType = Normalize(request.RelationshipType, LineageRelationshipTypes.Manual, "RelationshipType");
        var metadata = Metadata(request.Metadata);

        if (sourceType == targetType && request.SourceEntityId == request.TargetEntityId)
            throw Invalid("Self-referencing lineage relationships are not allowed.", "SelfReference");

        var source = await ResolveNodeAsync(sourceType, request.SourceEntityId, null, ct);
        var target = await ResolveNodeAsync(targetType, request.TargetEntityId, null, ct);
        if (source.DatasetId.HasValue) await EnsureDatasetAccessAsync(source.DatasetId.Value, DatasetAccessLevels.Write, ct);
        if (target.DatasetId.HasValue) await EnsureDatasetAccessAsync(target.DatasetId.Value, DatasetAccessLevels.Write, ct);
        if (source.WorkspaceId != target.WorkspaceId)
            throw Invalid("Cross-workspace lineage relationships are not allowed for any role.", "CrossWorkspaceLineage");
        if (source.DatasetId == target.DatasetId)
            throw Invalid("A dataset cannot create a dependency on itself or one of its own versions.", "SelfReference");

        await EnsureWorkspaceAsync(source.WorkspaceId, ct);
        if (await db.LineageRelationships.IgnoreQueryFilters().AnyAsync(x =>
                x.SourceEntityType == source.Type && x.SourceEntityId == source.Id &&
                x.TargetEntityType == target.Type && x.TargetEntityId == target.Id &&
                x.RelationshipType == relationshipType && x.IsActive, ct))
            throw Invalid("An active lineage relationship with the same source, target and type already exists.", "DuplicateLineage");

        if (await WouldCreateCycleAsync(source.WorkspaceId, source.DatasetId!.Value, target.DatasetId!.Value, ct))
            throw Invalid("The relationship would create a circular dataset dependency.", "CircularDependency");

        var relevant = target.RelevantVersionId.HasValue
            ? target
            : await CurrentDatasetVersionAsync(target.DatasetId.Value, ct);
        var entity = new LineageRelationship
        {
            WorkspaceId = target.WorkspaceId,
            DatasetId = target.DatasetId.Value,
            OwnerId = target.OwnerId,
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
            RelevantDatasetVersionId = relevant.RelevantVersionId,
            RelevantDatasetVersionNumber = relevant.Version,
            MetadataJson = metadata,
            IsAutomatic = false,
            IsActive = true,
            CreatedByUserId = currentUser.UserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LineageRelationships.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Lineage Relationship Created", "LineageRelationship", entity.Id.ToString(),
            $"Type={entity.RelationshipType}; Source={entity.SourceEntityType}/{entity.SourceEntityId}; Target={entity.TargetEntityType}/{entity.TargetEntityId}",
            workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ToRelationshipResponse(entity);
    }

    public async Task<LineageRelationshipResponse> UpdateAsync(
        Guid relationshipId, UpdateLineageRelationshipRequest request, CancellationToken ct)
    {
        RequireUser();
        var entity = await db.LineageRelationships.SingleOrDefaultAsync(x => x.Id == relationshipId, ct)
            ?? throw NotFound("Lineage relationship was not found or is not accessible.");
        await EnsureDatasetAccessAsync(entity.DatasetId, DatasetAccessLevels.Write, ct);
        if (!CanAccessWorkspace(entity.WorkspaceId)) throw NotFound("Lineage relationship was not found or is not accessible.");
        if (entity.IsAutomatic)
            throw Invalid("Automatically captured lineage is immutable.", "ImmutableAutomaticLineage", 409);
        if (!entity.IsActive)
            throw Invalid("An inactive lineage relationship cannot be updated.", "InactiveLineage", 409);
        entity.MetadataJson = Metadata(request.Metadata);
        entity.UpdatedByUserId = currentUser.UserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Lineage Relationship Updated", "LineageRelationship", entity.Id.ToString(),
            $"Type={entity.RelationshipType}; metadata updated.", workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ToRelationshipResponse(entity);
    }

    public async Task<LineageRelationshipResponse> DeactivateAsync(
        Guid relationshipId, DeactivateLineageRelationshipRequest request, CancellationToken ct)
    {
        RequireUser();
        var entity = await db.LineageRelationships.SingleOrDefaultAsync(x => x.Id == relationshipId, ct)
            ?? throw NotFound("Lineage relationship was not found or is not accessible.");
        await EnsureDatasetAccessAsync(entity.DatasetId, DatasetAccessLevels.Write, ct);
        if (!CanAccessWorkspace(entity.WorkspaceId)) throw NotFound("Lineage relationship was not found or is not accessible.");
        if (!entity.IsActive)
            throw Invalid("The lineage relationship is already inactive.", "InactiveLineage", 409);
        entity.IsActive = false;
        entity.DeactivatedByUserId = currentUser.UserId;
        entity.DeactivatedAtUtc = DateTime.UtcNow;
        entity.DeactivationReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("Lineage Relationship Removed", "LineageRelationship", entity.Id.ToString(),
            $"Type={entity.RelationshipType}; deactivated; Reason={entity.DeactivationReason}",
            workspaceId: entity.WorkspaceId, cancellationToken: ct);
        return ToRelationshipResponse(entity);
    }

    public async Task<LineageGraphResponse> DatasetAsync(
        Guid datasetId, string direction, LineageTraversalQuery query, CancellationToken ct)
    {
        await EnsureDatasetAccessAsync(datasetId, DatasetAccessLevels.Read, ct);
        var root = await ResolveNodeAsync(LineageEntityTypes.Dataset, datasetId, query.WorkspaceId, ct);
        return await TraverseAsync(root, direction, query, ct);
    }

    public async Task<LineageGraphResponse> DatasetVersionAsync(
        Guid datasetId, int versionNumber, LineageTraversalQuery query, CancellationToken ct)
    {
        await EnsureDatasetAccessAsync(datasetId, DatasetAccessLevels.Read, ct);
        if (versionNumber < 1) throw Invalid("VersionNumber must be positive.");
        await EnsureWorkspaceRequestAsync(query.WorkspaceId, ct);
        var version = await db.DatasetVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DatasetId == datasetId && x.VersionNumber == versionNumber, ct)
            ?? throw NotFound("Dataset version was not found or is not accessible.");
        var root = await ResolveNodeAsync(LineageEntityTypes.DatasetVersion, version.Id, query.WorkspaceId, ct);
        return await TraverseAsync(root, LineageDirections.Complete, query, ct);
    }

    public async Task<LineageGraphResponse> ImportAsync(Guid importId, LineageTraversalQuery query, CancellationToken ct)
    {
        var root = await ResolveNodeAsync(LineageEntityTypes.Import, importId, query.WorkspaceId, ct);
        if (root.DatasetId.HasValue) await EnsureDatasetAccessAsync(root.DatasetId.Value, DatasetAccessLevels.Read, ct);
        return await TraverseAsync(root, LineageDirections.Complete, query, ct);
    }

    public async Task<LineageGraphResponse> SourceAsync(
        string sourceType, Guid sourceId, LineageTraversalQuery query, CancellationToken ct)
    {
        var normalized = Normalize(sourceType, LineageEntityTypes.Sources, "SourceType");
        var root = await ResolveNodeAsync(normalized, sourceId, query.WorkspaceId, ct);
        if (root.DatasetId.HasValue) await EnsureDatasetAccessAsync(root.DatasetId.Value, DatasetAccessLevels.Read, ct);
        return await TraverseAsync(root, LineageDirections.Downstream, query, ct);
    }

    public async Task<ImpactAnalysisResponse> ImpactAsync(Guid datasetId, LineageTraversalQuery query, CancellationToken ct)
    {
        await EnsureDatasetAccessAsync(datasetId, DatasetAccessLevels.Read, ct);
        var root = await ResolveNodeAsync(LineageEntityTypes.Dataset, datasetId, query.WorkspaceId, ct);
        var activeQuery = new LineageTraversalQuery { WorkspaceId = query.WorkspaceId, Depth = query.Depth, IncludeInactive = false };
        var graph = await TraverseAsync(root, LineageDirections.Downstream, activeQuery, ct);
        var affected = graph.Nodes.Where(x => !(x.EntityType == root.Type && x.EntityId == root.Id)).ToList();
        await audit.WriteAsync("Impact Analysis Requested", "Dataset", datasetId.ToString(),
            $"Depth={graph.RequestedDepth}; Nodes={affected.Count}; Truncated={graph.Truncated}",
            workspaceId: root.WorkspaceId, cancellationToken: ct);
        return new(graph.Root, graph.RequestedDepth, graph.TraversedDepth, graph.Truncated,
            affected.Count(x => x.EntityType == LineageEntityTypes.Dataset),
            affected.Count(x => x.EntityType == LineageEntityTypes.DatasetVersion),
            affected.Count(x => x.EntityType == LineageEntityTypes.Import),
            affected.Count(x => x.EntityType == LineageEntityTypes.Transformation),
            affected.Count(x => x.EntityType == LineageEntityTypes.QualityProfile),
            affected, graph.Relationships);
    }

    private async Task<LineageGraphResponse> TraverseAsync(
        ResolvedNode root, string direction, LineageTraversalQuery query, CancellationToken ct)
    {
        await EnsureWorkspaceRequestAsync(query.WorkspaceId, ct);
        direction = Normalize(direction, LineageDirections.All, "Direction");
        var maxDepth = Math.Clamp(_options.MaxTraversalDepth, 1, 10);
        var requestedDepth = query.Depth ?? Math.Clamp(_options.DefaultTraversalDepth, 1, maxDepth);
        if (requestedDepth is < 1 || requestedDepth > maxDepth)
            throw Invalid($"Depth must be between 1 and {maxDepth}.");
        var maxNodes = Math.Clamp(_options.MaxGraphNodes, 10, 10000);

        var rootKey = new NodeKey(root.Type, root.Id);
        var nodes = new Dictionary<NodeKey, LineageNodeResponse>
        {
            [rootKey] = ToNode(root, 0)
        };
        var edges = new Dictionary<Guid, LineageEdgeResponse>();
        var frontier = new HashSet<NodeKey> { rootKey };
        var traversedDepth = 0;
        var truncated = false;

        for (var level = 1; level <= requestedDepth && frontier.Count > 0; level++)
        {
            var ids = frontier.Select(x => x.Id).Distinct().ToArray();
            var candidates = db.LineageRelationships.AsNoTracking()
                .Where(x => x.WorkspaceId == root.WorkspaceId &&
                            db.Datasets.Any(d => d.Id == x.DatasetId) &&
                            (ids.Contains(x.SourceEntityId) || ids.Contains(x.TargetEntityId)));
            if (!query.IncludeInactive) candidates = candidates.Where(x => x.IsActive);
            var batch = await candidates.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
            var next = new HashSet<NodeKey>();

            foreach (var edge in batch)
            {
                var sourceKey = new NodeKey(edge.SourceEntityType, edge.SourceEntityId);
                var targetKey = new NodeKey(edge.TargetEntityType, edge.TargetEntityId);
                var sourceMatch = frontier.Contains(sourceKey);
                var targetMatch = frontier.Contains(targetKey);
                var followDownstream = sourceMatch &&
                    (direction == LineageDirections.Downstream || direction == LineageDirections.Complete);
                var followUpstream = targetMatch &&
                    (direction == LineageDirections.Upstream || direction == LineageDirections.Complete);
                if (!followDownstream && !followUpstream) continue;

                var neighbor = followDownstream ? targetKey : sourceKey;
                if (!nodes.ContainsKey(neighbor))
                {
                    if (nodes.Count >= maxNodes) { truncated = true; continue; }
                    nodes[neighbor] = followDownstream ? TargetNode(edge, level) : SourceNode(edge, level);
                    next.Add(neighbor);
                }
                edges.TryAdd(edge.Id, ToEdge(edge));
            }
            if (next.Count > 0) traversedDepth = level;
            frontier = next;
        }

        if (!truncated && frontier.Count > 0 && traversedDepth == requestedDepth)
            truncated = await HasUnvisitedNeighborAsync(root.WorkspaceId, frontier, nodes.Keys, direction,
                query.IncludeInactive, ct);
        var rootResponse = nodes[rootKey];
        return new(rootResponse, direction, requestedDepth, traversedDepth, truncated,
            nodes.Values.OrderBy(x => x.Depth).ThenBy(x => x.EntityType).ThenBy(x => x.DisplayName).ToList(),
            edges.Values.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.RelationshipId).ToList());
    }

    private async Task<bool> HasUnvisitedNeighborAsync(Guid workspaceId, HashSet<NodeKey> frontier,
        ICollection<NodeKey> visited, string direction, bool includeInactive, CancellationToken ct)
    {
        var ids = frontier.Select(x => x.Id).Distinct().ToArray();
        var candidates = db.LineageRelationships.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId &&
                        (ids.Contains(x.SourceEntityId) || ids.Contains(x.TargetEntityId)));
        if (!includeInactive) candidates = candidates.Where(x => x.IsActive);
        var batch = await candidates.ToListAsync(ct);
        var known = visited.ToHashSet();
        return batch.Any(edge =>
        {
            var source = new NodeKey(edge.SourceEntityType, edge.SourceEntityId);
            var target = new NodeKey(edge.TargetEntityType, edge.TargetEntityId);
            return ((direction == LineageDirections.Downstream || direction == LineageDirections.Complete) &&
                    frontier.Contains(source) && !known.Contains(target)) ||
                   ((direction == LineageDirections.Upstream || direction == LineageDirections.Complete) &&
                    frontier.Contains(target) && !known.Contains(source));
        });
    }

    private async Task<ResolvedNode> ResolveNodeAsync(string entityType, Guid entityId, Guid? requestedWorkspaceId, CancellationToken ct)
    {
        NotEmpty(entityId, "EntityId");
        await EnsureWorkspaceRequestAsync(requestedWorkspaceId, ct);
        entityType = Normalize(entityType, LineageEntityTypes.All, "EntityType");
        ResolvedNode? node = entityType switch
        {
            LineageEntityTypes.Dataset => await ResolveDatasetAsync(entityId, ct),
            LineageEntityTypes.DatasetVersion => await ResolveVersionAsync(entityId, ct),
            LineageEntityTypes.SourceFile => await ResolveFileAsync(entityId, ct),
            LineageEntityTypes.Import => await ResolveImportAsync(entityId, ct),
            LineageEntityTypes.Transformation => await ResolveTransformationAsync(entityId, ct),
            LineageEntityTypes.QualityProfile => await ResolveProfileAsync(entityId, ct),
            LineageEntityTypes.DataSource => await ResolveCapturedSourceAsync(entityId, ct),
            _ => null
        };
        if (node is null || !CanAccessWorkspace(node.WorkspaceId) ||
            requestedWorkspaceId.HasValue && node.WorkspaceId != requestedWorkspaceId)
            throw NotFound("Lineage entity was not found or is not accessible.");
        return node;
    }

    private async Task<ResolvedNode?> ResolveDatasetAsync(Guid id, CancellationToken ct)
    {
        var item = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var version = await db.DatasetVersions.AsNoTracking().Where(x => x.DatasetId == id && x.IsCurrent)
            .Select(x => new { x.Id, x.VersionNumber }).SingleOrDefaultAsync(ct);
        return new(LineageEntityTypes.Dataset, item.Id, item.WorkspaceId, item.Id, item.OwnerId,
            $"{item.Code} - {item.Name}", version?.VersionNumber, version?.Id);
    }

    private async Task<ResolvedNode?> ResolveVersionAsync(Guid id, CancellationToken ct)
    {
        var item = await db.DatasetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? null : new(LineageEntityTypes.DatasetVersion, item.Id, item.WorkspaceId,
            item.DatasetId, item.OwnerId, $"{item.Code} v{item.VersionNumber}", item.VersionNumber, item.Id);
    }

    private async Task<ResolvedNode?> ResolveFileAsync(Guid id, CancellationToken ct)
    {
        var item = await db.UploadedDataFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.DatasetId, ct);
        return dataset is null ? null : new(LineageEntityTypes.SourceFile, item.Id, item.WorkspaceId,
            item.DatasetId, dataset.OwnerId, item.OriginalFileName, null, null);
    }

    private async Task<ResolvedNode?> ResolveImportAsync(Guid id, CancellationToken ct)
    {
        var item = await db.DataImports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.DatasetId, ct);
        return dataset is null ? null : new(LineageEntityTypes.Import, item.Id, item.WorkspaceId,
            item.DatasetId, dataset.OwnerId, $"Import {item.Id:N}", null, null);
    }

    private async Task<ResolvedNode?> ResolveTransformationAsync(Guid id, CancellationToken ct)
    {
        var item = await db.DatasetTransformationConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.DatasetId, ct);
        return dataset is null ? null : new(LineageEntityTypes.Transformation, item.Id, item.WorkspaceId,
            item.DatasetId, dataset.OwnerId, $"Transformation configuration v{item.Version}", item.Version, null);
    }

    private async Task<ResolvedNode?> ResolveProfileAsync(Guid id, CancellationToken ct)
    {
        var item = await db.DataQualityProfileRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.DatasetId, ct);
        return dataset is null ? null : new(LineageEntityTypes.QualityProfile, item.Id, item.WorkspaceId,
            item.DatasetId, dataset.OwnerId, $"Quality profile {item.Id:N}", null, null);
    }

    private async Task<ResolvedNode?> ResolveCapturedSourceAsync(Guid id, CancellationToken ct)
    {
        var edge = await db.LineageRelationships.AsNoTracking()
            .Where(x => x.SourceEntityType == LineageEntityTypes.DataSource && x.SourceEntityId == id)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return edge is null ? null : new(LineageEntityTypes.DataSource, id, edge.WorkspaceId,
            edge.SourceDatasetId, edge.OwnerId, edge.SourceDisplayName, null, edge.RelevantDatasetVersionId);
    }

    private async Task<ResolvedNode> CurrentDatasetVersionAsync(Guid datasetId, CancellationToken ct)
    {
        var version = await db.DatasetVersions.AsNoTracking().SingleAsync(x => x.DatasetId == datasetId && x.IsCurrent, ct);
        return await ResolveVersionAsync(version.Id, ct) ?? throw NotFound("Current dataset version was not found.");
    }

    private async Task<bool> WouldCreateCycleAsync(Guid workspaceId, Guid sourceDatasetId, Guid targetDatasetId, CancellationToken ct)
    {
        var edges = await db.LineageRelationships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.RelationshipType == LineageRelationshipTypes.Feeds &&
                        x.IsActive && x.SourceDatasetId != null && x.TargetDatasetId != null)
            .Select(x => new { Source = x.SourceDatasetId!.Value, Target = x.TargetDatasetId!.Value }).ToListAsync(ct);
        var adjacency = edges.GroupBy(x => x.Source).ToDictionary(x => x.Key, x => x.Select(y => y.Target).Distinct().ToArray());
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        pending.Enqueue(targetDatasetId);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current)) continue;
            if (current == sourceDatasetId) return true;
            if (adjacency.TryGetValue(current, out var next))
                foreach (var id in next) pending.Enqueue(id);
        }
        return false;
    }

    private async Task ValidateSearchAsync(LineageSearchQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw Invalid("Page must be positive, PageSize must be 1-100, and the offset must fit Int32.");
        if (query.DatasetVersion is < 1) throw Invalid("DatasetVersion must be positive.");
        foreach (var id in new[] { query.WorkspaceId, query.DatasetId, query.ImportId, query.CreatedByUserId, query.OwnerId })
            if (id == Guid.Empty) throw Invalid("Empty GUID filters are not allowed.");
        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc >= query.ToUtc)
            throw Invalid("FromUtc must be earlier than ToUtc; ToUtc is exclusive.");
        if (query.SortDirection is not ("asc" or "desc")) throw Invalid("SortDirection must be asc or desc.");
        if (query.SortBy is not null && !new[] { "createdAtUtc", "relationshipType", "sourceType", "targetType", "sourceName", "targetName" }.Contains(query.SortBy))
            throw Invalid("SortBy is not supported.");
        await EnsureWorkspaceRequestAsync(query.WorkspaceId, ct);
    }

    private async Task EnsureWorkspaceRequestAsync(Guid? workspaceId, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) throw new AnalyticsRequestException(401, "Unauthenticated", "Authentication is required.");
        if (!currentUser.IsPlatformAdministrator &&
            (!currentUser.WorkspaceId.HasValue || workspaceId.HasValue && workspaceId != currentUser.WorkspaceId))
            throw new AnalyticsRequestException(403, "WorkspaceForbidden", "The requested workspace is not accessible.");
        if (workspaceId.HasValue) await EnsureWorkspaceAsync(workspaceId.Value, ct);
    }

    private async Task EnsureWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        if (!await db.Workspaces.AsNoTracking().AnyAsync(x => x.Id == workspaceId, ct))
            throw NotFound("Workspace was not found or is not accessible.");
    }

    private async Task EnsureDatasetAccessAsync(Guid datasetId, string level, CancellationToken ct)
    {
        if (datasetAccess is null) return; // Backward-compatible direct construction in legacy tests.
        var result = await datasetAccess.AuthorizeAsync(datasetId, level, ct);
        if (!result.Succeeded)
            throw new AnalyticsRequestException(result.StatusCode, "DatasetAccessDenied", result.Error ?? "Dataset access denied.");
    }

    private void RequireUser()
    {
        if (!currentUser.UserId.HasValue)
            throw new AnalyticsRequestException(401, "Unauthenticated", "Authentication is required.");
    }

    private bool CanAccessWorkspace(Guid workspaceId) => currentUser.IsPlatformAdministrator ||
        currentUser.WorkspaceId.HasValue && currentUser.WorkspaceId.Value == workspaceId;

    private static string Metadata(JsonElement? metadata)
    {
        if (!metadata.HasValue || metadata.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "{}";
        if (metadata.Value.ValueKind != JsonValueKind.Object) throw Invalid("Metadata must be a JSON object.");
        var value = metadata.Value.GetRawText();
        if (value.Length > 4000) throw Invalid("Metadata cannot exceed 4000 characters.");
        return value;
    }

    private static string Normalize(string value, IReadOnlyCollection<string> allowed, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{parameter} is required.");
        return allowed.FirstOrDefault(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw Invalid($"{parameter} must be one of: {string.Join(", ", allowed)}.");
    }

    private static string? NormalizeOptional(string? value, IReadOnlyCollection<string> allowed, string parameter) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, allowed, parameter);

    private static void NotEmpty(Guid value, string parameter)
    {
        if (value == Guid.Empty) throw Invalid($"{parameter} cannot be an empty GUID.");
    }

    private static AnalyticsRequestException Invalid(string message, string code = "InvalidParameters", int status = 400) =>
        new(status, code, message);
    private static AnalyticsRequestException NotFound(string message) => new(404, "LineageNotFound", message);

    private static IReadOnlyDictionary<string, object?> ParseMetadata(string value)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, object?>>(value) ?? new Dictionary<string, object?>(); }
        catch (JsonException) { return new Dictionary<string, object?>(); }
    }

    private static LineageRelationshipResponse ToRelationshipResponse(LineageRelationship x) => new(
        x.Id, x.WorkspaceId, x.DatasetId, x.OwnerId, SourceNode(x, 0), TargetNode(x, 0), x.RelationshipType,
        x.RelevantDatasetVersionId, x.RelevantDatasetVersionNumber, x.ProcessEntityType, x.ProcessEntityId,
        x.IsAutomatic, x.IsActive, x.CreatedByUserId, x.CreatedAtUtc, x.UpdatedByUserId, x.UpdatedAtUtc,
        x.DeactivatedByUserId, x.DeactivatedAtUtc, x.DeactivationReason, ParseMetadata(x.MetadataJson));

    private static LineageEdgeResponse ToEdge(LineageRelationship x) => new(
        x.Id, x.RelationshipType, "SourceToTarget", x.SourceEntityType, x.SourceEntityId,
        x.TargetEntityType, x.TargetEntityId, x.DatasetId, x.RelevantDatasetVersionId,
        x.RelevantDatasetVersionNumber, x.ProcessEntityType, x.ProcessEntityId, x.IsAutomatic,
        x.IsActive, x.CreatedAtUtc, ParseMetadata(x.MetadataJson));

    private static LineageNodeResponse ToNode(ResolvedNode x, int depth) => new(
        x.Type, x.Id, x.DisplayName, x.WorkspaceId, x.DatasetId, x.Version, depth,
        NodeMetadata(x.DatasetId, x.Version));

    private static LineageNodeResponse SourceNode(LineageRelationship x, int depth) => new(
        x.SourceEntityType, x.SourceEntityId, x.SourceDisplayName, x.WorkspaceId,
        x.SourceDatasetId, x.SourceVersionNumber, depth, NodeMetadata(x.SourceDatasetId, x.SourceVersionNumber));

    private static LineageNodeResponse TargetNode(LineageRelationship x, int depth) => new(
        x.TargetEntityType, x.TargetEntityId, x.TargetDisplayName, x.WorkspaceId,
        x.TargetDatasetId, x.TargetVersionNumber, depth, NodeMetadata(x.TargetDatasetId, x.TargetVersionNumber));

    private static IReadOnlyDictionary<string, object?> NodeMetadata(Guid? datasetId, int? version) =>
        new Dictionary<string, object?> { ["datasetId"] = datasetId, ["version"] = version };

    private sealed record NodeKey(string Type, Guid Id);
    private sealed record ResolvedNode(string Type, Guid Id, Guid WorkspaceId, Guid? DatasetId,
        Guid OwnerId, string DisplayName, int? Version, Guid? RelevantVersionId);
}
