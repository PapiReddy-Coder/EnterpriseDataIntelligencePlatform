
using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController, Route("api/audit")]
public sealed class AuditController(AppDbContext db, ICurrentUser current) : ControllerBase 
{ 
    [HttpGet, HasPermission(Permissions.AnalyticsView)] 
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 50) 
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable(); 
        if (!current.IsPlatformAdministrator) q = q.Where(x => x.WorkspaceId == current.WorkspaceId); 
        return Ok(await q.OrderByDescending(x => x.CreatedAtUtc).Skip((Math.Max(page, 1) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100)).ToListAsync()); 
    }
}
