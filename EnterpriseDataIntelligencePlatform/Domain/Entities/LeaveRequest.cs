namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class LeaveRequest : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string Status { get; set; } = "Pending";
}
