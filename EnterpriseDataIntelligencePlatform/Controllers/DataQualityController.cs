using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api")]
public sealed class DataQualityController(IDataQualityProfileService quality) : ControllerBase
{
    [HttpPost("datasets/{datasetId:guid}/quality-profiles")]
    [HasPermission(Permissions.QualityProfilesRun)]
    public async Task<IActionResult> Start(Guid datasetId, CancellationToken ct) =>
        Result(await quality.StartManualAsync(datasetId, ct));

    [HttpGet("quality-profiles/{profileRunId:guid}")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Status(Guid profileRunId, CancellationToken ct) =>
        Result(await quality.GetAsync(profileRunId, ct));

    [HttpPost("quality-profiles/{profileRunId:guid}/cancel")]
    [HasPermission(Permissions.QualityProfilesRun)]
    public async Task<IActionResult> Cancel(Guid profileRunId, CancellationToken ct) =>
        Result(await quality.CancelAsync(profileRunId, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-profiles/latest")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Latest(Guid datasetId, CancellationToken ct) =>
        Result(await quality.LatestAsync(datasetId, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-profiles")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> History(Guid datasetId, [FromQuery] Guid? importId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Result(await quality.HistoryAsync(datasetId, importId, page, pageSize, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-score")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Score(Guid datasetId, CancellationToken ct) =>
        Result(await quality.LatestAsync(datasetId, ct));

    [HttpGet("quality-profiles/{profileRunId:guid}/columns")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Columns(Guid profileRunId, CancellationToken ct) =>
        Result(await quality.ColumnsAsync(profileRunId, ct));

    [HttpGet("quality-profiles/{profileRunId:guid}/issues")]
    [HasPermission(Permissions.QualityIssuesView)]
    public async Task<IActionResult> Issues(Guid profileRunId, [FromQuery] string? issueType,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Result(await quality.IssuesAsync(profileRunId, issueType, page, pageSize, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-issues")]
    [HasPermission(Permissions.QualityIssuesView)]
    public async Task<IActionResult> DatasetIssues(Guid datasetId, [FromQuery] Guid? importId,
        [FromQuery] string? issueType, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Result(await quality.DatasetIssuesAsync(datasetId, importId, issueType, page, pageSize, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-trend")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Trend(Guid datasetId, [FromQuery] int limit = 30, CancellationToken ct = default) =>
        Result(await quality.TrendAsync(datasetId, limit, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-comparison")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Compare(Guid datasetId, [FromQuery] Guid leftProfileRunId,
        [FromQuery] Guid rightProfileRunId, CancellationToken ct) =>
        Result(await quality.CompareAsync(datasetId, leftProfileRunId, rightProfileRunId, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-dashboard")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> Dashboard(Guid datasetId, [FromQuery] int issueLimit = 5,
        [FromQuery] int trendLimit = 30, CancellationToken ct = default) =>
        Result(await quality.DashboardAsync(datasetId, issueLimit, trendLimit, ct));

    [HttpGet("datasets/{datasetId:guid}/quality-thresholds")]
    [HasPermission(Permissions.QualityProfilesView)]
    public async Task<IActionResult> GetThresholds(Guid datasetId, CancellationToken ct) =>
        Result(await quality.GetThresholdAsync(datasetId, ct));

    [HttpPut("datasets/{datasetId:guid}/quality-thresholds")]
    [HasPermission(Permissions.QualityThresholdsManage)]
    public async Task<IActionResult> UpsertThresholds(Guid datasetId, UpsertQualityThresholdRequest request,
        CancellationToken ct) => Result(await quality.UpsertThresholdAsync(datasetId, request, ct));

    [HttpDelete("datasets/{datasetId:guid}/quality-thresholds")]
    [HasPermission(Permissions.QualityThresholdsManage)]
    public async Task<IActionResult> DeleteThresholds(Guid datasetId, CancellationToken ct) =>
        Result(await quality.DeleteThresholdAsync(datasetId, ct));

    private IActionResult Result<T>(ServiceResult<T> result) => result.Succeeded && result.Value is not null
        ? StatusCode(result.StatusCode, result.Value)
        : StatusCode(result.StatusCode, new { status = result.StatusCode, message = result.Error ?? "Request failed." });
}
