namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
    public ICollection<Dataset> Datasets { get; set; } = new List<Dataset>();
}
