using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/data-sharing")]
public sealed class DataSharingController(IDataSharingService sharing) : ControllerBase
{
    [HttpPost("exports")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> CreateExport(CreateExportRequest request, CancellationToken ct)
    {
        var result = await sharing.CreateExportAsync(request, ct);
        return Result(result);
    }

    [HttpGet("exports/{exportId:guid}")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> GetExport(Guid exportId, CancellationToken ct) => Result(await sharing.GetExportAsync(exportId, ct));

    [HttpPost("exports/{exportId:guid}/cancel")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> CancelExport(Guid exportId, CancellationToken ct) => Result(await sharing.CancelExportAsync(exportId, ct));

    [HttpGet("exports")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> SearchExports([FromQuery] ExportHistorySearchRequest request, CancellationToken ct) => Result(await sharing.SearchExportsAsync(request, ct));

    [HttpGet("exports/dashboard")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> ExportDashboard(CancellationToken ct) => Result(await sharing.DashboardAsync(ct));

    [HttpGet("exports/{exportId:guid}/download")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> Download(Guid exportId, CancellationToken ct)
    {
        var result = await sharing.DownloadExportAsync(exportId, ct);
        if (!result.Succeeded) return Problem(statusCode: result.StatusCode, title: "Export download failed", detail: result.Error);
        return File(result.Value!.Stream, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("shares")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> Share(CreateDatasetShareRequest request, CancellationToken ct)
    {
        var result = await sharing.ShareDatasetAsync(request, ct);
        return Result(result);
    }

    [HttpPut("shares/{grantId:guid}")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> UpdateShare(Guid grantId, CreateDatasetShareRequest request, CancellationToken ct)
    {
        var result = await sharing.UpdateShareAsync(grantId, request, ct);
        return Result(result);
    }

    [HttpPost("shares/{grantId:guid}/revoke")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> RevokeShare(Guid grantId, RevokeDatasetShareRequest request, CancellationToken ct)
    {
        var result = await sharing.RevokeShareAsync(grantId, request, ct);
        return Result(result);
    }

    [HttpGet("shares")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> SearchShares([FromQuery] ShareHistorySearchRequest request, CancellationToken ct) => Result(await sharing.SearchSharesAsync(request, ct));

    private IActionResult Result<T>(ServiceResult<T> result)
    {
        if (result.Succeeded) return StatusCode(result.StatusCode, result.Value);
        return Problem(statusCode: result.StatusCode, title: "Data sharing operation failed", detail: result.Error);
    }
}
