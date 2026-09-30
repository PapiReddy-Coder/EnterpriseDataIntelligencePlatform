using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/governance")]
public sealed class GovernanceController(IGovernanceService governance) : ControllerBase
{
    [HttpPut("datasets/{datasetId:guid}")]
    [HasPermission(Permissions.GovernanceManage)]
    public Task<IActionResult> Upsert(Guid datasetId, UpsertDatasetGovernanceRequest request, CancellationToken ct) =>
        Result(governance.UpsertAsync(datasetId, request, ct));

    [HttpGet("datasets/{datasetId:guid}")]
    [HasPermission(Permissions.GovernanceView)]
    public Task<IActionResult> Get(Guid datasetId, CancellationToken ct) => Result(governance.GetAsync(datasetId, ct));

    [HttpPatch("datasets/{datasetId:guid}/classification")]
    [HasPermission(Permissions.GovernanceManage)]
    public Task<IActionResult> Classification(Guid datasetId, UpdateClassificationRequest request, CancellationToken ct) =>
        Result(governance.UpdateClassificationAsync(datasetId, request, ct));

    [HttpPatch("datasets/{datasetId:guid}/steward")]
    [HasPermission(Permissions.GovernanceManage)]
    public Task<IActionResult> Steward(Guid datasetId, AssignGovernanceUsersRequest request, CancellationToken ct) =>
        Result(governance.AssignUsersAsync(datasetId, request, ct));

    [HttpPost("datasets/{datasetId:guid}/certifications")]
    [HasPermission(Permissions.GovernanceManage)]
    public Task<IActionResult> SubmitCertification(Guid datasetId, SubmitCertificationRequest request, CancellationToken ct) =>
        Result(governance.SubmitCertificationAsync(datasetId, request, ct));

    [HttpPost("certifications/{certificationId:guid}/review")]
    [HasPermission(Permissions.GovernanceCertify)]
    public Task<IActionResult> ReviewCertification(Guid certificationId, ReviewCertificationRequest request, CancellationToken ct) =>
        Result(governance.ReviewCertificationAsync(certificationId, request, ct));

    [HttpGet("datasets/{datasetId:guid}/certifications")]
    [HasPermission(Permissions.GovernanceView)]
    public Task<IActionResult> CertificationHistory(Guid datasetId, CancellationToken ct) =>
        Result(governance.CertificationHistoryAsync(datasetId, ct));

    [HttpPost("datasets/{datasetId:guid}/access-requests")]
    [HasPermission(Permissions.DatasetAccessRequest)]
    public Task<IActionResult> RequestAccess(Guid datasetId, CreateDatasetAccessRequest request, CancellationToken ct) =>
        Result(governance.RequestAccessAsync(datasetId, request, ct));

    [HttpPost("access-requests/{requestId:guid}/review")]
    [HasPermission(Permissions.DatasetAccessReview)]
    public Task<IActionResult> ReviewAccess(Guid requestId, ReviewAccessRequest request, CancellationToken ct) =>
        Result(governance.ReviewAccessAsync(requestId, request, ct));

    [HttpPost("access-requests/{requestId:guid}/revoke")]
    [HasPermission(Permissions.DatasetAccessRevoke)]
    public Task<IActionResult> Revoke(Guid requestId, RevokeDatasetAccessRequest request, CancellationToken ct) =>
        Result(governance.RevokeAccessAsync(requestId, request, ct));

    [HttpGet("access-requests")]
    [HasPermission(Permissions.DatasetAccessRequest)]
    public Task<IActionResult> AccessRequests([FromQuery] AccessRequestSearchRequest request, CancellationToken ct) =>
        Result(governance.AccessRequestsAsync(request, ct));

    [HttpGet("access-requests/{requestId:guid}/history")]
    [HasPermission(Permissions.DatasetAccessRequest)]
    public Task<IActionResult> AccessHistory(Guid requestId, CancellationToken ct) =>
        Result(governance.AccessHistoryAsync(requestId, ct));

    [HttpGet("datasets")]
    [HasPermission(Permissions.GovernanceView)]
    public Task<IActionResult> Search([FromQuery] GovernanceSearchRequest request, CancellationToken ct) =>
        Result(governance.SearchAsync(request, ct));

    [HttpGet("dashboard")]
    [HasPermission(Permissions.GovernanceDashboard)]
    public Task<IActionResult> Dashboard(CancellationToken ct) => Result(governance.DashboardAsync(ct));

    private async Task<IActionResult> Result<T>(Task<ServiceResult<T>> operation)
    {
        var result = await operation;
        if (result.Succeeded) return StatusCode(result.StatusCode, result.Value);
        return Problem(statusCode: result.StatusCode, title: "Governance operation failed", detail: result.Error);
    }
}
