using Microsoft.AspNetCore.Identity;

namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Dataset> OwnedDatasets { get; set; } = new List<Dataset>();
}
