namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataQualityIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileRunId { get; set; }
    public Guid DatasetId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ImportId { get; set; }
    public Guid? DatasetRecordId { get; set; }
    public Guid? DatasetColumnId { get; set; }
    public string? ColumnName { get; set; }
    public string IssueType { get; set; } = QualityIssueTypes.ValidationFailure;
    public string Severity { get; set; } = "Error";
    public string? ValidationRule { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? InvalidValue { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DataQualityProfileRun ProfileRun { get; set; } = null!;
}
