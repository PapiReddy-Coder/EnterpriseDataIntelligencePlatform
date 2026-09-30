using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = AnalyticsPolicies.Read)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 400)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 401)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 403)]
[ProducesResponseType(typeof(AnalyticsResponse<object>), 413)]
public sealed class ReportsController(IReportService reports, IReportExporter exporter, IAuditService audit) : ControllerBase
{
    /// <summary>Generate dataset-summary, data-quality, import-activity, data-processing, or workspace-summary.</summary>
    [HttpGet("{reportType}")]
    [Produces("application/json")]
    public async Task<ActionResult<AnalyticsResponse<ReportData>>> Generate(
        string reportType, [FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        var result = await reports.GenerateAsync(reportType, query, false, ct);
        return Ok(AnalyticsResponses.Success(HttpContext, result.Data, query, result.Pagination));
    }

    /// <summary>Export a report as csv or xlsx. Scope=all is bounded by MaxExportRows; Scope=page exports the requested page.</summary>
    [HttpGet("{reportType}/export")]
    [Authorize(Policy = AnalyticsPolicies.Export)]
    [Produces("text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Export(string reportType, [FromQuery] ReportExportQuery query, CancellationToken ct)
    {
        if (query.Format is not ("csv" or "xlsx")) AnalyticsRules.Invalid("Format must be csv or xlsx.");
        if (query.Scope is not ("all" or "page")) AnalyticsRules.Invalid("Scope must be all or page.");
        var report = await reports.GenerateAsync(reportType, query, query.Scope == "all", ct);
        var file = exporter.Export(report.Data, query.Format, ct);
        await audit.WriteAsync("Report Exported", "AnalyticsReport", reportType,
            $"Format={query.Format}; Scope={query.Scope}; Rows={report.Data.Rows.Count}; FromUtc={query.FromUtc:O}; ToUtc={query.ToUtc:O}",
            workspaceId: query.WorkspaceId, cancellationToken: ct);
        Response.Headers["X-Report-Row-Count"] = report.Data.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Report-Total-Count"] = report.Pagination.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Report-Scope"] = query.Scope;
        Response.Headers["X-Report-Timezone"] = "UTC";
        return File(file.Bytes, file.ContentType, file.FileName);
    }
}
