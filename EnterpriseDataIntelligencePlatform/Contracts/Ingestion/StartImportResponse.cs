namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record StartImportResponse(Guid ImportId, string Status, DateTime QueuedAtUtc);
