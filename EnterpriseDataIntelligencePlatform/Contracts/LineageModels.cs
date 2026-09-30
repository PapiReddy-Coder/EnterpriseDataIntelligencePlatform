using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed class LineageSearchQuery
{
    public Guid? WorkspaceId { get; set; }
    public Guid? DatasetId { get; set; }
    public string? SourceType { get; set; }
    public string? TargetType { get; set; }
    public string? RelationshipType { get; set; }
    public int? DatasetVersion { get; set; }
    public Guid? ImportId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public bool? IsActive { get; set; } = true;
    [StringLength(200)] public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "desc";
}

public sealed class LineageTraversalQuery
{
    public Guid? WorkspaceId { get; set; }
    [Range(1, 10)] public int? Depth { get; set; }
    public bool IncludeInactive { get; set; }
}

public sealed record CreateLineageRelationshipRequest(
    [Required] string SourceEntityType,
    Guid SourceEntityId,
    [Required] string TargetEntityType,
    Guid TargetEntityId,
    string RelationshipType = "Feeds",
    JsonElement? Metadata = null);

public sealed record UpdateLineageRelationshipRequest(JsonElement? Metadata);

public sealed record DeactivateLineageRelationshipRequest(
    [Required, StringLength(1000, MinimumLength = 3)] string Reason);

public sealed record LineageNodeResponse(
    string EntityType,
    Guid EntityId,
    string DisplayName,
    Guid WorkspaceId,
    Guid? DatasetId,
    int? Version,
    int Depth,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record LineageEdgeResponse(
    Guid RelationshipId,
    string RelationshipType,
    string Direction,
    string SourceEntityType,
    Guid SourceEntityId,
    string TargetEntityType,
    Guid TargetEntityId,
    Guid DatasetId,
    Guid? RelevantDatasetVersionId,
    int? RelevantDatasetVersion,
    string? ProcessEntityType,
    Guid? ProcessEntityId,
    bool IsAutomatic,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record LineageGraphResponse(
    LineageNodeResponse Root,
    string Direction,
    int RequestedDepth,
    int TraversedDepth,
    bool Truncated,
    IReadOnlyList<LineageNodeResponse> Nodes,
    IReadOnlyList<LineageEdgeResponse> Relationships);

public sealed record LineageRelationshipResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid DatasetId,
    Guid OwnerId,
    LineageNodeResponse Source,
    LineageNodeResponse Target,
    string RelationshipType,
    Guid? RelevantDatasetVersionId,
    int? RelevantDatasetVersion,
    string? ProcessEntityType,
    Guid? ProcessEntityId,
    bool IsAutomatic,
    bool IsActive,
    Guid? CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid? UpdatedByUserId,
    DateTime? UpdatedAtUtc,
    Guid? DeactivatedByUserId,
    DateTime? DeactivatedAtUtc,
    string? DeactivationReason,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record ImpactAnalysisResponse(
    LineageNodeResponse Root,
    int RequestedDepth,
    int TraversedDepth,
    bool Truncated,
    int AffectedDatasetCount,
    int AffectedDatasetVersionCount,
    int AffectedImportCount,
    int AffectedTransformationCount,
    int AffectedQualityProfileCount,
    IReadOnlyList<LineageNodeResponse> AffectedNodes,
    IReadOnlyList<LineageEdgeResponse> Relationships);
