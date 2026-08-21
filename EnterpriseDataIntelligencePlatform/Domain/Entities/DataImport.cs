namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid FileId { get; set; }
    public Guid InitiatedByUserId { get; set; }
    public string Status { get; set; } = ImportStatuses.Created;
    public string ImportMode { get; set; } = ImportModes.Full;
    public string DuplicateBehavior { get; set; } = DuplicateBehaviors.Reject;
    public string InvalidRecordBehavior { get; set; } = InvalidRecordBehaviors.Skip;
    public string CsvDelimiter { get; set; } = ",";
    public bool FirstRowContainsHeaders { get; set; } = true;
    public string? WorksheetName { get; set; }
    public string KeyColumnsJson { get; set; } = "[]";
    public int TotalRecords { get; set; }
    public int SuccessfullyImportedRecords { get; set; }
    public int RejectedRecords { get; set; }
    public int ErrorCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureMessage { get; set; }
    public bool CancellationRequested { get; set; }
    public Guid? TransformationConfigurationId { get; set; }
    public int? TransformationConfigurationVersion { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public UploadedDataFile File { get; set; } = null!;
    public DatasetTransformationConfiguration? TransformationConfiguration { get; set; }
}
