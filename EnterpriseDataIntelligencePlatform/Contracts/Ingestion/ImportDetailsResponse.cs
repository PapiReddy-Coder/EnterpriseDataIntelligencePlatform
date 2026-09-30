namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record ImportDetailsResponse(
    Guid ImportId,
    Guid DatasetId,
    Guid FileId,
    string FileName,
    string ImportMode,
    string DuplicateBehavior,
    string Status,
    int TotalRecords,
    int SuccessfullyImportedRecords,
    int RejectedRecords,
    int ErrorCount,
    DateTime CreatedAtUtc,
    DateTime? StartTimeUtc,
    DateTime? CompletionTimeUtc,
    Guid InitiatedBy,
    string? WorksheetName,
    IReadOnlyList<string> KeyColumns,
    Guid? TransformationConfigurationId,
    int? TransformationConfigurationVersion,
    string? FailureMessage);
