using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api")]
public sealed class TransformationsController(ITransformationService service) : ControllerBase
{
    [HttpPost("datasets/{datasetId:guid}/transformation-configurations")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> Save(Guid datasetId, SaveTransformationConfigurationRequest request, CancellationToken ct) => Result(await service.SaveAsync(datasetId, request, ct));

    [HttpGet("datasets/{datasetId:guid}/transformation-configurations/active")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> Active(Guid datasetId, CancellationToken ct) => Result(await service.GetActiveAsync(datasetId, ct));

    [HttpGet("datasets/{datasetId:guid}/transformation-configurations/history")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> History(Guid datasetId, CancellationToken ct) => Result(await service.HistoryAsync(datasetId, ct));

    [HttpDelete("datasets/{datasetId:guid}/transformation-configurations/{configurationId:guid}")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> Delete(Guid datasetId, Guid configurationId, CancellationToken ct) => Result(await service.DeleteAsync(datasetId, configurationId, ct));

    [HttpPost("imports/{importId:guid}/transformation-preview")]
    [HasPermission(Permissions.ImportsView)]
    public async Task<IActionResult> Preview(Guid importId, TransformationPreviewRequest request, CancellationToken ct) => Result(await service.PreviewAsync(importId, request.Limit, ct));

    private IActionResult Result<T>(ServiceResult<T> result) => result.Succeeded && result.Value is not null
        ? StatusCode(result.StatusCode, result.Value)
        : StatusCode(result.StatusCode, new { status = result.StatusCode, message = result.Error ?? "Request failed." });
}
