namespace EnterpriseDataIntelligencePlatform.Domain;

/// <summary>
/// Immutable-by-default lineage edge. Source/target display and version values are snapshots so
/// historical lineage remains understandable after later metadata changes.
/// </summary>
public sealed class LineageRelationship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Guid OwnerId { get; set; }

    public string SourceEntityType { get; set; } = string.Empty;
    public Guid SourceEntityId { get; set; }
    public Guid? SourceDatasetId { get; set; }
    public string SourceDisplayName { get; set; } = string.Empty;
    public int? SourceVersionNumber { get; set; }

    public string TargetEntityType { get; set; } = string.Empty;
    public Guid TargetEntityId { get; set; }
    public Guid? TargetDatasetId { get; set; }
    public string TargetDisplayName { get; set; } = string.Empty;
    public int? TargetVersionNumber { get; set; }

    public string RelationshipType { get; set; } = string.Empty;
    public Guid? RelevantDatasetVersionId { get; set; }
    public int? RelevantDatasetVersionNumber { get; set; }
    public string? ProcessEntityType { get; set; }
    public Guid? ProcessEntityId { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public bool IsAutomatic { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? DeactivatedByUserId { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
    public string? DeactivationReason { get; set; }

    public Dataset Dataset { get; set; } = null!;
    public DatasetVersion? RelevantDatasetVersion { get; set; }
}
