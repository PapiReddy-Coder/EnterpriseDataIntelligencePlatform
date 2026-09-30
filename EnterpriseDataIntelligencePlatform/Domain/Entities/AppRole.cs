using Microsoft.AspNetCore.Identity;

namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class AppRole : IdentityRole<Guid>
{
    public bool IsGlobal { get; set; }
    public string Description { get; set; } = string.Empty;
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
