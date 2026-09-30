
using EnterpriseDataIntelligencePlatform.Data; 
using Microsoft.EntityFrameworkCore; 
using System.Security.Claims;

namespace EnterpriseDataIntelligencePlatform.Infrastructure;
public static class TokenValidationEvents 
{ 
    public static async Task ValidateAsync(Microsoft.AspNetCore.Authentication.JwtBearer.TokenValidatedContext ctx)
    { 
        var db=ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>(); 
        var uidText=ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); 
        var sidText=ctx.Principal?.FindFirstValue("session_id"); 
        var stamp=ctx.Principal?.FindFirstValue("security_stamp"); 
        if(!Guid.TryParse(uidText,out var uid)||!Guid.TryParse(sidText,out var sid))
        {
            ctx.Fail("Invalid token context.");
            return;
        } 
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==uid); 
        if(user is null||!user.IsActive||user.SecurityStamp!=stamp)
        {
            ctx.Fail("User session is no longer valid.");
            return;
        } 
        if(!await db.RefreshTokens.AsNoTracking().AnyAsync(x=>x.UserId==uid&&x.SessionId==sid&&x.RevokedAtUtc==null&&x.ExpiresAtUtc>DateTime.UtcNow))
        {
            ctx.Fail("Session revoked.");
            return;
        } 
        if(user.WorkspaceId is Guid wid&&!await db.Workspaces.AsNoTracking().AnyAsync(x=>x.Id==wid&&x.IsActive))
        {
            ctx.Fail("Workspace inactive.");
        }
    }
}
