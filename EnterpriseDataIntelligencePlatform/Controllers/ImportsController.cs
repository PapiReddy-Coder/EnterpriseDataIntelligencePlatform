using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api")]
public sealed class ImportsController(IImportService imports) : ControllerBase
{
    [HttpPost("datasets/{datasetId:guid}/imports/files")]
    [HasPermission(Permissions.ImportsUpload)]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid datasetId, IFormFile file, CancellationToken ct) => Result(await imports.UploadAsync(datasetId, file, ct));

    [HttpPost("datasets/{datasetId:guid}/imports")]
    [HasPermission(Permissions.ImportsCreate)]
    public async Task<IActionResult> Create(Guid datasetId, CreateImportRequest request, CancellationToken ct) => Result(await imports.CreateAsync(datasetId, request, ct));

    [HttpPost("imports/{importId:guid}/start")]
    [HasPermission(Permissions.ImportsStart)]
    public async Task<IActionResult> Start(Guid importId, CancellationToken ct) => Result(await imports.StartAsync(importId, ct));

    [HttpGet("imports/{importId:guid}")]
    [HasPermission(Permissions.ImportsView)]
    public async Task<IActionResult> Get(Guid importId, CancellationToken ct) => Result(await imports.GetAsync(importId, ct));

    [HttpGet("imports")]
    [HasPermission(Permissions.ImportsView)]
    public async Task<IActionResult> History([FromQuery] Guid? datasetId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) => Result(await imports.HistoryAsync(datasetId, status, page, pageSize, ct));

    [HttpGet("imports/{importId:guid}/errors")]
    [HasPermission(Permissions.ImportsErrorsView)]
    public async Task<IActionResult> Errors(Guid importId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => Result(await imports.ErrorsAsync(importId, page, pageSize, ct));

    [HttpPost("imports/{importId:guid}/cancel")]
    [HasPermission(Permissions.ImportsCancel)]
    public async Task<IActionResult> Cancel(Guid importId, CancellationToken ct) => Result(await imports.CancelAsync(importId, ct));

    [HttpGet("datasets/{datasetId:guid}/import-schema")]
    [HasPermission(Permissions.ImportsView)]
    public async Task<IActionResult> Schema(Guid datasetId, CancellationToken ct) => Result(await imports.GetSchemaAsync(datasetId, ct));

    [HttpPut("datasets/{datasetId:guid}/import-schema/key-columns")]
    [HasPermission(Permissions.ImportsSchemaManage)]
    public async Task<IActionResult> Keys(Guid datasetId, UpdateDatasetKeyColumnsRequest request, CancellationToken ct) => Result(await imports.UpdateKeyColumnsAsync(datasetId, request, ct));

    private IActionResult Result<T>(ServiceResult<T> result) => result.Succeeded && result.Value is not null
        ? StatusCode(result.StatusCode, result.Value)
        : StatusCode(result.StatusCode, new { status = result.StatusCode, message = result.Error ?? "Request failed." });
}
