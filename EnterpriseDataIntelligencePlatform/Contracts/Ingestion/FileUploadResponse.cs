namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record FileUploadResponse(
    Guid FileId,
    Guid DatasetId,
    string FileName,
    long FileSizeBytes,
    IReadOnlyList<string> AvailableSheets,
    DateTime UploadedAtUtc);
