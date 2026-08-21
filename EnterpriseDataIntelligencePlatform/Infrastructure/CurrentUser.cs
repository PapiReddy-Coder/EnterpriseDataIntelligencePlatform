
using System.Security.Claims;
using EnterpriseDataIntelligencePlatform.Domain;
namespace EnterpriseDataIntelligencePlatform.Infrastructure;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? WorkspaceId { get; }
    Guid? SessionId { get; }
    bool IsPlatformAdministrator { get; }
}
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser 
{ 
    ClaimsPrincipal? U => accessor.HttpContext?.User; 
    Guid? Parse(string c) => Guid.TryParse(U?.FindFirstValue(c), out var v) ? v : null; 
    public Guid? UserId => Parse(ClaimTypes.NameIdentifier); 
    public Guid? WorkspaceId => Parse("workspace_id"); 
    public Guid? SessionId => Parse("session_id"); 
    public bool IsPlatformAdministrator => U?.IsInRole(Roles.PlatformAdministrator) == true; 
}
