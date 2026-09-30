using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed class DatasetAccessPolicy(AppDbContext db, ICurrentUser currentUser) : IDatasetAccessPolicy
{
    public async Task<ServiceResult<bool>> AuthorizeAsync(Guid datasetId, string requiredAccessLevel, CancellationToken ct)
    {
        if (!DatasetAccessLevels.All.Contains(requiredAccessLevel, StringComparer.OrdinalIgnoreCase))
            return ServiceResult<bool>.Failure("Invalid required dataset access level.", StatusCodes.Status500InternalServerError);
        if (!currentUser.UserId.HasValue)
            return ServiceResult<bool>.Failure("Authentication is required.", StatusCodes.Status401Unauthorized);

        var dataset = await db.Datasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == datasetId && !x.IsDeleted)
            .Select(x => new
            {
                x.WorkspaceId, x.OwnerId,
                Classification = x.Governance == null ? DataClassifications.Internal : x.Governance.Classification,
                StewardId = x.Governance == null ? null : x.Governance.DataStewardId
            }).SingleOrDefaultAsync(ct);
        if (dataset is null || (!currentUser.IsPlatformAdministrator && dataset.WorkspaceId != currentUser.WorkspaceId))
            return ServiceResult<bool>.Failure("Dataset was not found.", StatusCodes.Status404NotFound);

        if (currentUser.IsPlatformAdministrator || await IsWorkspaceAdministratorAsync(ct) ||
            dataset.OwnerId == currentUser.UserId || dataset.StewardId == currentUser.UserId)
            return ServiceResult<bool>.Success(true);

        if (!DataClassifications.RequiresGrant(dataset.Classification))
            return ServiceResult<bool>.Success(true);

        var now = DateTime.UtcNow;
        var grants = await db.DatasetAccessGrants.AsNoTracking()
            .Where(x => x.DatasetId == datasetId && x.UserId == currentUser.UserId &&
                        x.Status == AccessGrantStatuses.Active &&
                        (!x.ExpiryDateUtc.HasValue || x.ExpiryDateUtc > now))
            .Select(x => x.AccessLevel).ToListAsync(ct);
        var allowed = grants.Any(x => DatasetAccessLevels.Rank(x) >= DatasetAccessLevels.Rank(requiredAccessLevel));
        return allowed
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Failure("You do not have the required dataset access. Submit an access request.", StatusCodes.Status403Forbidden);
    }

    public async Task<bool> IsWorkspaceAdministratorAsync(CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return false;
        var id = currentUser.UserId.Value;
        return await db.UserRoles.Where(x => x.UserId == id)
            .Join(db.Roles, x => x.RoleId, x => x.Id, (_, role) => role.Name)
            .AnyAsync(x => x == Roles.WorkspaceAdministrator, ct);
    }

    public async Task<bool> IsDatasetReviewerAsync(Guid datasetId, CancellationToken ct)
    {
        if (!currentUser.UserId.HasValue) return false;
        if (currentUser.IsPlatformAdministrator || await IsWorkspaceAdministratorAsync(ct)) return true;
        var id = currentUser.UserId.Value;
        return await db.Datasets.IgnoreQueryFilters().AnyAsync(x => x.Id == datasetId && !x.IsDeleted &&
            x.WorkspaceId == currentUser.WorkspaceId &&
            (x.OwnerId == id || (x.Governance != null && x.Governance.DataStewardId == id)), ct);
    }
}
