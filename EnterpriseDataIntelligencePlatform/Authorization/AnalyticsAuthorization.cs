using System.Security.Claims;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Authorization;

public static class AnalyticsPolicies
{
    public const string Read = "Analytics.Read";
    public const string Export = "Analytics.Export";
}

public sealed record AnalyticsPermissionRequirement(bool Export) : IAuthorizationRequirement;

public sealed class AnalyticsPermissionHandler(AppDbContext db) : AuthorizationHandler<AnalyticsPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AnalyticsPermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        var allowed = requirement.Export ? new[] { Permissions.ReportsExport } :
            new[] { Permissions.AnalyticsView, Permissions.DashboardsView, Permissions.ReportsRead, Permissions.ReportsGenerate };
        var permissions = from userRole in db.UserRoles
                          join rolePermission in db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
                          join permission in db.Permissions on rolePermission.PermissionId equals permission.Id
                          where userRole.UserId == id
                          select permission.Name;
        if (await permissions.AnyAsync(x => allowed.Contains(x))) context.Succeed(requirement);
    }
}
