using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController, Route("api/roles")]
public sealed class RolesController(AppDbContext db) : ControllerBase 
{
    [HttpGet] 
    public async Task<IActionResult> Get() => Ok(await db.Roles.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.IsGlobal, x.Description, Permissions = x.RolePermissions.Select(rp => rp.Permission.Name) }).ToListAsync()); 
    [HttpPut("{roleId:guid}/permissions"), HasPermission(Permissions.RolesAssignAll)] 
    public IActionResult NotSupported(Guid roleId) => BadRequest("Only predefined role-permission mappings are supported.");
}
