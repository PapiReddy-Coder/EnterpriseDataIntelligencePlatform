using EnterpriseDataIntelligencePlatform.Contracts;

namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface ILineageService
{
    Task<AnalyticsPage<LineageRelationshipResponse>> SearchAsync(LineageSearchQuery query, CancellationToken ct);
    Task<LineageRelationshipResponse> GetRelationshipAsync(Guid relationshipId, CancellationToken ct);
    Task<LineageRelationshipResponse> CreateAsync(CreateLineageRelationshipRequest request, CancellationToken ct);
    Task<LineageRelationshipResponse> UpdateAsync(Guid relationshipId, UpdateLineageRelationshipRequest request, CancellationToken ct);
    Task<LineageRelationshipResponse> DeactivateAsync(Guid relationshipId, DeactivateLineageRelationshipRequest request, CancellationToken ct);
    Task<LineageGraphResponse> DatasetAsync(Guid datasetId, string direction, LineageTraversalQuery query, CancellationToken ct);
    Task<LineageGraphResponse> DatasetVersionAsync(Guid datasetId, int versionNumber, LineageTraversalQuery query, CancellationToken ct);
    Task<LineageGraphResponse> ImportAsync(Guid importId, LineageTraversalQuery query, CancellationToken ct);
    Task<LineageGraphResponse> SourceAsync(string sourceType, Guid sourceId, LineageTraversalQuery query, CancellationToken ct);
    Task<ImpactAnalysisResponse> ImpactAsync(Guid datasetId, LineageTraversalQuery query, CancellationToken ct);
}

public interface ILineageCaptureService
{
    Task CaptureDatasetVersionAsync(Guid datasetVersionId, CancellationToken ct);
    Task CaptureTransformationAsync(Guid transformationId, CancellationToken ct);
    Task CaptureImportAsync(Guid importId, CancellationToken ct);
    Task CaptureQualityProfileAsync(Guid profileRunId, CancellationToken ct);
}
