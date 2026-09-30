
namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IAuditService
{
    Task WriteAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null,
        Guid? userId = null,
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default);
}
