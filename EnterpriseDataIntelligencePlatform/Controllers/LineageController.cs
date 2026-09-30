using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/lineage")]
[Produces("application/json")]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 400)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 401)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 403)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 404)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 409)]
public sealed class LineageController(ILineageService lineage) : ControllerBase
{
    /// <summary>Search active or historical lineage relationships with filtering, sorting and pagination.</summary>
    [HttpGet("relationships")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<LineageRelationshipResponse>>>> Search(
        [FromQuery] LineageSearchQuery query, CancellationToken ct)
    {
        var result = await lineage.SearchAsync(query, ct);
        return Ok(AnalyticsResponses.Success(HttpContext, result.Items, query, result.Pagination));
    }

    /// <summary>Retrieve one lineage relationship, including inactive historical records.</summary>
    [HttpGet("relationships/{relationshipId:guid}")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageRelationshipResponse>>> GetRelationship(
        Guid relationshipId, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.GetRelationshipAsync(relationshipId, ct)));

    /// <summary>Create a manual same-workspace, acyclic dataset or dataset-version dependency.</summary>
    [HttpPost("relationships")]
    [HasPermission(Permissions.LineageManage)]
    public async Task<ActionResult<AnalyticsResponse<LineageRelationshipResponse>>> Create(
        CreateLineageRelationshipRequest request, CancellationToken ct)
    {
        var result = await lineage.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetRelationship), new { relationshipId = result.Id },
            AnalyticsResponses.Success(HttpContext, result));
    }

    /// <summary>Update metadata on a manually created active relationship. Automatic history is immutable.</summary>
    [HttpPut("relationships/{relationshipId:guid}")]
    [HasPermission(Permissions.LineageManage)]
    public async Task<ActionResult<AnalyticsResponse<LineageRelationshipResponse>>> Update(
        Guid relationshipId, UpdateLineageRelationshipRequest request, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.UpdateAsync(relationshipId, request, ct)));

    /// <summary>Deactivate a relationship while preserving the historical record.</summary>
    [HttpDelete("relationships/{relationshipId:guid}")]
    [HasPermission(Permissions.LineageManage)]
    public async Task<ActionResult<AnalyticsResponse<LineageRelationshipResponse>>> Deactivate(
        Guid relationshipId, DeactivateLineageRelationshipRequest request, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.DeactivateAsync(relationshipId, request, ct)));

    /// <summary>Return complete upstream and downstream lineage for a dataset.</summary>
    [HttpGet("datasets/{datasetId:guid}")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> Dataset(
        Guid datasetId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext,
            await lineage.DatasetAsync(datasetId, LineageDirections.Complete, query, ct), query));

    /// <summary>Return only upstream lineage for a dataset.</summary>
    [HttpGet("datasets/{datasetId:guid}/upstream")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> Upstream(
        Guid datasetId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext,
            await lineage.DatasetAsync(datasetId, LineageDirections.Upstream, query, ct), query));

    /// <summary>Return only downstream lineage for a dataset.</summary>
    [HttpGet("datasets/{datasetId:guid}/downstream")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> Downstream(
        Guid datasetId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext,
            await lineage.DatasetAsync(datasetId, LineageDirections.Downstream, query, ct), query));

    /// <summary>Return lineage anchored at a specific immutable dataset metadata/schema version.</summary>
    [HttpGet("datasets/{datasetId:guid}/versions/{versionNumber:int}")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> DatasetVersion(
        Guid datasetId, int versionNumber, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext,
            await lineage.DatasetVersionAsync(datasetId, versionNumber, query, ct), query));

    /// <summary>Trace a source file, optional transformation, import and target dataset version.</summary>
    [HttpGet("imports/{importId:guid}")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> Import(
        Guid importId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.ImportAsync(importId, query, ct), query));

    /// <summary>Trace a DataSource or SourceFile node downstream. Source IDs are returned by graph/search APIs.</summary>
    [HttpGet("sources/{sourceType}/{sourceId:guid}")]
    [HasPermission(Permissions.LineageView)]
    public async Task<ActionResult<AnalyticsResponse<LineageGraphResponse>>> Source(
        string sourceType, Guid sourceId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.SourceAsync(sourceType, sourceId, query, ct), query));

    /// <summary>Return active downstream entities that may be affected by a dataset change.</summary>
    [HttpGet("datasets/{datasetId:guid}/impact")]
    [HasPermission(Permissions.LineageImpact)]
    public async Task<ActionResult<AnalyticsResponse<ImpactAnalysisResponse>>> Impact(
        Guid datasetId, [FromQuery] LineageTraversalQuery query, CancellationToken ct) =>
        Ok(AnalyticsResponses.Success(HttpContext, await lineage.ImpactAsync(datasetId, query, ct), query));
}
