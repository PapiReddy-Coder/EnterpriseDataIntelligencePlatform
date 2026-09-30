using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/datasets")]
public sealed class DatasetsController(
    IDatasetService datasets) : ControllerBase
{
    [HttpPost]
    [HasPermission(Permissions.DatasetsCreate)]
    public async Task<IActionResult> Register(
        RegisterDatasetRequest request,
        CancellationToken ct)
    {
        var result = await datasets.RegisterAsync(request, ct);

        return ToActionResult(
            result,
            value => CreatedAtAction(
                nameof(Get),
                new { id = value.Id },
                value));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateDatasetRequest request,
        CancellationToken ct)
    {
        var result = await datasets.UpdateAsync(id, request, ct);

        return ToActionResult(
            result,
            value => Ok(new
            {
                message = "Dataset updated successfully.",
                dataset = value
            }));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> Get(
        Guid id,
        [FromQuery] bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var result = await datasets.GetAsync(
            id,
            includeDeleted,
            ct);

        return ToActionResult(result, Ok);
    }

    [HttpGet]
    [HasPermission(Permissions.DatasetsView)]
    public async Task<IActionResult> Search(
        [FromQuery] DatasetSearchRequest request,
        CancellationToken ct)
    {
        var result = await datasets.SearchAsync(
            request,
            ct);

        return ToActionResult(result, Ok);
    }

    [HttpPatch("{id:guid}/activate")]
    [HasPermission(Permissions.DatasetsUpdate)]
    public async Task<IActionResult> Activate(
        Guid id,
        DatasetLifecycleRequest request,
        CancellationToken ct)
    {
        var result = await datasets.ActivateAsync(
            id,
            request,
            ct);

        return ToLifecycleActionResult(
            result,
            id,
            DatasetStatuses.Active,
            "Dataset activated successfully.");
    }

    [HttpPatch("{id:guid}/archive")]
    [HasPermission(Permissions.DatasetsArchive)]
    public async Task<IActionResult> Archive(
        Guid id,
        DatasetLifecycleRequest request,
        CancellationToken ct)
    {
        var result = await datasets.ArchiveAsync(
            id,
            request,
            ct);

        return ToLifecycleActionResult(
            result,
            id,
            DatasetStatuses.Archived,
            "Dataset archived successfully.");
    }

    [HttpPatch("{id:guid}/restore")]
    [HasPermission(Permissions.DatasetsRestore)]
    public async Task<IActionResult> RestoreArchived(
        Guid id,
        DatasetLifecycleRequest request,
        CancellationToken ct)
    {
        var result = await datasets.RestoreArchivedAsync(
            id,
            request,
            ct);

        return ToLifecycleActionResult(
            result,
            id,
            DatasetStatuses.Active,
            "Dataset restored successfully.");
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.DatasetsDelete)]
    public async Task<IActionResult> SoftDelete(
        Guid id,
        [FromBody] DatasetLifecycleRequest request,
        CancellationToken ct)
    {
        var result = await datasets.SoftDeleteAsync(
            id,
            request,
            ct);

        return ToLifecycleActionResult(
            result,
            id,
            "Deleted",
            "Dataset soft deleted successfully.");
    }

    [HttpPatch("{id:guid}/recover")]
    [HasPermission(Permissions.DatasetsRestore)]
    public async Task<IActionResult> Recover(
        Guid id,
        DatasetLifecycleRequest request,
        CancellationToken ct)
    {
        var result = await datasets.RecoverAsync(
            id,
            request,
            ct);

        return ToLifecycleActionResult(
            result,
            id,
            "Recovered",
            "Dataset recovered successfully.");
    }

    [HttpGet("{id:guid}/versions")]
    [HasPermission(Permissions.DatasetVersionsView)]
    public async Task<IActionResult> Versions(
        Guid id,
        CancellationToken ct)
    {
        var result = await datasets.GetVersionsAsync(
            id,
            ct);

        return ToActionResult(result, Ok);
    }

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    [HasPermission(Permissions.DatasetVersionsView)]
    public async Task<IActionResult> Version(
        Guid id,
        int versionNumber,
        CancellationToken ct)
    {
        if (versionNumber <= 0)
        {
            return BadRequest(new
            {
                message = "Version number must be greater than zero."
            });
        }

        var result = await datasets.GetVersionAsync(
            id,
            versionNumber,
            ct);

        return ToActionResult(result, Ok);
    }

    private IActionResult ToLifecycleActionResult(
        ServiceResult<bool> result,
        Guid datasetId,
        string status,
        string message)
    {
        if (!result.Succeeded)
        {
            return ProblemResult(
                result.Error ?? "Dataset operation failed.",
                result.StatusCode);
        }

        return Ok(new
        {
            datasetId,
            status,
            message
        });
    }

    private IActionResult ToActionResult<T>(
        ServiceResult<T> result,
        Func<T, IActionResult> success)
    {
        if (result.Succeeded &&
            result.Value is not null)
        {
            return success(result.Value);
        }

        return ProblemResult(
            result.Error ?? "Request failed.",
            result.StatusCode);
    }

    private IActionResult ProblemResult(
        string message,
        int statusCode)
    {
        return StatusCode(statusCode, new
        {
            status = statusCode,
            message
        });
    }
}