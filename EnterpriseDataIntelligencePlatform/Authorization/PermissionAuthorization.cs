using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using EnterpriseDataIntelligencePlatform.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace EnterpriseDataIntelligencePlatform.Authorization;
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute($"Permission:{permission}");
public sealed record PermissionRequirement(string Permission):IAuthorizationRequirement;
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options):DefaultAuthorizationPolicyProvider(options){ public override async Task<AuthorizationPolicy?> GetPolicyAsync(string name){ if(name.StartsWith("Permission:",StringComparison.OrdinalIgnoreCase)) return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(name[11..])).Build(); return await base.GetPolicyAsync(name); } }
public sealed class PermissionAuthorizationHandler(AppDbContext db):AuthorizationHandler<PermissionRequirement>{ protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement req){ var id=context.User.FindFirstValue(ClaimTypes.NameIdentifier); if(!Guid.TryParse(id,out var userId)) return; var allowed=await db.UserRoles.Where(ur=>ur.UserId==userId).Join(db.RolePermissions,ur=>ur.RoleId,rp=>rp.RoleId,(ur,rp)=>rp).Join(db.Permissions,rp=>rp.PermissionId,p=>p.Id,(rp,p)=>p.Name).AnyAsync(x=>x==req.Permission); if(allowed) context.Succeed(req); } }
