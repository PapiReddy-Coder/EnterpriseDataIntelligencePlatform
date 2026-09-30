
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class AuditService(AppDbContext dbContext, ICurrentUser currentUser) : IAuditService
{
    public async Task WriteAsync(
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null,
        Guid? userId = null,
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress,
            UserId = userId ?? currentUser.UserId,
            WorkspaceId = workspaceId ?? currentUser.WorkspaceId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
