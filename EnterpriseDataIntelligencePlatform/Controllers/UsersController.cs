
using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace EnterpriseDataIntelligencePlatform.Controllers;

[ApiController, Route("api/users")]
public sealed class UsersController(UserManager<AppUser> users, RoleManager<AppRole> roles, AppDbContext db, ICurrentUser current, IJwtTokenService tokens, IAuditService audit) : ControllerBase
{
    [HttpPost, HasPermission(Permissions.UsersManageWorkspace)] 
    public async Task<IActionResult> Create(CreateUserRequest r) 
    {
        var isPlatform = current.IsPlatformAdministrator; var wid = isPlatform ? r.WorkspaceId : current.WorkspaceId; 
        if (r.Role == Roles.PlatformAdministrator && !isPlatform) 
            return Forbid(); 
        if (r.Role != Roles.PlatformAdministrator && wid is null) 
            return BadRequest("Workspace is required."); 
        if (!isPlatform && r.WorkspaceId != null && r.WorkspaceId != current.WorkspaceId) 
            return Forbid(); 
        if (!await roles.RoleExistsAsync(r.Role)) 
            return BadRequest("Invalid predefined role."); 
        var role = await roles.FindByNameAsync(r.Role); 
        if (role!.IsGlobal && wid is not null)
            return BadRequest("Global role cannot be assigned to a workspace user."); 
        var u = new AppUser { UserName = r.Email, Email = r.Email, FullName = r.FullName, WorkspaceId = role.IsGlobal ? null : wid, EmailConfirmed = true }; 
        var created = await users.CreateAsync(u, r.Password); 
        if (!created.Succeeded) 
            return BadRequest(created.Errors); 
        await users.AddToRoleAsync(u, r.Role); 
        await audit.WriteAsync("UserCreated", "User", u.Id.ToString(), r.Role); 
        return Created($"api/users/{u.Id}", new { u.Id, u.Email, u.FullName, u.WorkspaceId, Role = r.Role }); 
    }
    [HttpGet, HasPermission(Permissions.UsersManageWorkspace)] 
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, 
    [FromQuery] string? search = null, 
    
    [FromQuery] bool? active = null) { page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); 
        var q = db.Users.AsNoTracking().AsQueryable(); if (!current.IsPlatformAdministrator) q = q.Where(x => x.WorkspaceId == current.WorkspaceId); 
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Email!.Contains(search) || x.FullName.Contains(search)); 
        if (active.HasValue) q = q.Where(x => x.IsActive == active); 
        var count = await q.CountAsync(); 
        var items = await q.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new { x.Id, x.Email, x.FullName, x.WorkspaceId, x.IsActive }).ToListAsync(); 
        return Ok(new PagedResponse<object>(items, page, pageSize, count)); }
    [HttpPatch("{id:guid}/status"), HasPermission(Permissions.UsersManageWorkspace)] 
    public async Task<IActionResult> Status(Guid id, [FromQuery] bool active) 
    {
        var u = await users.FindByIdAsync(id.ToString()); 
        if (u is null) return NotFound(); 
        if (!current.IsPlatformAdministrator && u.WorkspaceId != current.WorkspaceId) 
            return Forbid(); u.IsActive = active; await users.UpdateAsync(u); 
        if (!active) await tokens.RevokeAllAsync(u.Id); 
        await audit.WriteAsync(active ? "UserActivated" : "UserDeactivated", "User", id.ToString()); 
        return NoContent(); 
    }
    [HttpPut("{id:guid}/role"), HasPermission(Permissions.RolesAssignWorkspace)] 
    public async Task<IActionResult> Role(Guid id, AssignRoleRequest r) 
    {
        var u = await users.FindByIdAsync(id.ToString()); 
        if (u is null) return NotFound(); 
        if (!current.IsPlatformAdministrator && (u.WorkspaceId != current.WorkspaceId || r.Role == Roles.PlatformAdministrator)) 
            return Forbid(); var role = await roles.FindByNameAsync(r.Role); 
        if (role is null) return BadRequest("Invalid predefined role."); 
        if (role.IsGlobal && u.WorkspaceId is not null) 
            return BadRequest("Global role requires a platform user."); 
        var old = await users.GetRolesAsync(u); 
        await users.RemoveFromRolesAsync(u, old); 
        await users.AddToRoleAsync(u, r.Role); 
        await users.UpdateSecurityStampAsync(u); 
        await tokens.RevokeAllAsync(u.Id); 
        await audit.WriteAsync("RoleAssigned", "User", u.Id.ToString(), r.Role); 
        return NoContent(); 
    }
}
