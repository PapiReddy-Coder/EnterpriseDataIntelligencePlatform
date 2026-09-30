using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Policy = AnalyticsPolicies.Read)]
[Produces("application/json")]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 400)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 401)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 403)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 404)]
public sealed class AnalyticsController(IAnalyticsService analytics, IAuditService audit, ICurrentUser user) : ControllerBase
{
    /// <summary>Current dataset/quality KPIs plus date-filtered ingestion statistics.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AnalyticsResponse<DashboardSummary>>> Summary([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Result(await analytics.DashboardAsync(query, ct), query, ct);

    /// <summary>Dataset analytics, including the latest quality scores and attention flags.</summary>
    [HttpGet("datasets")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<DatasetAnalyticsRow>>>> Datasets([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.DatasetsAsync(query, false, ct), query, ct);

    /// <summary>Dataset counts grouped by status, category, workspace, or owner.</summary>
    [HttpGet("datasets/distribution")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<DatasetDistribution>>>> Distribution(
        [FromQuery] AnalyticsQuery query, CancellationToken ct, [FromQuery] string groupBy = "status") =>
        await Page(await analytics.DistributionAsync(query, groupBy, ct), query, ct);

    /// <summary>Most recently created or updated datasets. Activity is created or updated.</summary>
    [HttpGet("datasets/recent")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<DatasetAnalyticsRow>>>> Recent(
        [FromQuery] AnalyticsQuery query, CancellationToken ct, [FromQuery] string activity = "created")
    {
        if (activity is not ("created" or "updated")) AnalyticsRules.Invalid("Activity must be created or updated.");
        if (query.DateField is not null && query.DateField != activity) AnalyticsRules.Invalid("DateField must match Activity.");
        if (query.SortBy is not null && query.SortBy != activity + "AtUtc") AnalyticsRules.Invalid("SortBy must match Activity.");
        query.DateField = activity; query.SortBy = activity + "AtUtc";
        return await Page(await analytics.DatasetsAsync(query, false, ct), query, ct);
    }

    /// <summary>Rank datasets by import count in the requested activity period.</summary>
    [HttpGet("datasets/frequent-imports")]
    [HttpGet("imports/by-dataset")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<DatasetImportActivity>>>> ImportsByDataset(
        [FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.ImportsByDatasetAsync(query, ct), query, ct);

    /// <summary>Datasets requiring attention, with machine-readable reasons.</summary>
    [HttpGet("attention")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<AttentionDataset>>>> Attention([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.AttentionAsync(query, ct), query, ct);

    /// <summary>Simple averages of latest completed dataset-level profiles; missing scores remain null.</summary>
    [HttpGet("quality/summary")]
    public async Task<ActionResult<AnalyticsResponse<QualitySummary>>> QualitySummary([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Result(await analytics.QualitySummaryAsync(query, ct), query, ct);

    /// <summary>Per-dataset quality details with applied thresholds, issue counts, and profile freshness.</summary>
    [HttpGet("quality/datasets")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<DatasetAnalyticsRow>>>> QualityDatasets(
        [FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.DatasetsAsync(query, true, ct), query, ct);

    /// <summary>Top issue types/severities from latest profiles only. Never includes problematic values.</summary>
    [HttpGet("quality/issues")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<QualityIssueAggregate>>>> Issues(
        [FromQuery] AnalyticsQuery query, CancellationToken ct, [FromQuery] string? issueType = null, [FromQuery] string? severity = null) =>
        await Page(await analytics.IssuesAsync(query, issueType, severity, ct), query, ct);

    /// <summary>Daily/weekly/monthly quality trends. Latest run per dataset per bucket, then equal dataset weighting.</summary>
    [HttpGet("quality/trends")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<QualityTrendPoint>>>> QualityTrends(
        [FromQuery] TrendQuery query, CancellationToken ct) =>
        await Page(await analytics.QualityTrendAsync(query, ct), query, ct);

    /// <summary>Import statuses, attempted/processed/rejected counts, success rates, and processing times.</summary>
    [HttpGet("imports/summary")]
    public async Task<ActionResult<AnalyticsResponse<ImportSummary>>> ImportSummary([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Result(await analytics.ImportSummaryAsync(query, ct), query, ct);

    /// <summary>Paginated ingestion activity. Status filters dataset status; ImportStatus filters imports.</summary>
    [HttpGet("imports")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<ImportAnalyticsRow>>>> Imports([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.ImportsAsync(query, ct), query, ct);

    /// <summary>Ingestion metrics grouped daily, weekly, or monthly in UTC.</summary>
    [HttpGet("imports/trends")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<ImportTrendPoint>>>> ImportTrends(
        [FromQuery] TrendQuery query, CancellationToken ct) =>
        await Page(await analytics.ImportTrendAsync(query, ct), query, ct);

    /// <summary>Workspace summaries, including empty workspaces. Non-platform users see only their own workspace.</summary>
    [HttpGet("workspaces")]
    public async Task<ActionResult<AnalyticsResponse<IReadOnlyList<WorkspaceSummary>>>> Workspaces(
        [FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Page(await analytics.WorkspacesAsync(query, ct), query, ct);

    /// <summary>Workspace summary and a paginated dataset-category distribution.</summary>
    [HttpGet("workspaces/{workspaceId:guid}")]
    public async Task<ActionResult<AnalyticsResponse<WorkspaceSummary>>> Workspace(
        Guid workspaceId, [FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        await Result(await analytics.WorkspaceAsync(workspaceId, query, ct), query, ct);

    private async Task<ActionResult<AnalyticsResponse<T>>> Result<T>(T value, AnalyticsQuery query, CancellationToken ct,
        AnalyticsPagination? pagination = null)
    {
        if (user.IsPlatformAdministrator)
            await audit.WriteAsync("Analytics Access", "Analytics", Request.Path,
                $"PrivilegedAccess=True; WorkspaceId={query.WorkspaceId}; DatasetId={query.DatasetId}",
                workspaceId: query.WorkspaceId, cancellationToken: ct);
        return Ok(AnalyticsResponses.Success(HttpContext, value, query, pagination));
    }

    private Task<ActionResult<AnalyticsResponse<IReadOnlyList<T>>>> Page<T>(AnalyticsPage<T> value, AnalyticsQuery query, CancellationToken ct) =>
        Result(value.Items, query, ct, value.Pagination);
}
