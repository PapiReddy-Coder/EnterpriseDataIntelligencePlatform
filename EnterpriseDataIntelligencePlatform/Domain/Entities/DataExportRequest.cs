namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataExportRequest : IWorkspaceOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid? DatasetVersionId { get; set; }
    public DatasetVersion? DatasetVersion { get; set; }
    public Guid RequestedByUserId { get; set; }
    public AppUser RequestedByUser { get; set; } = null!;
    public string Format { get; set; } = DataExportFormats.Csv;
    public string SelectedColumnIdsJson { get; set; } = "[]";
    public string FiltersJson { get; set; } = "[]";
    public string SortsJson { get; set; } = "[]";
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string Status { get; set; } = DataExportStatuses.Pending;
    public long? RecordCount { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessingStartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? StorageKey { get; set; }
    public string? FileName { get; set; }
    public string? FailureReason { get; set; }
}
