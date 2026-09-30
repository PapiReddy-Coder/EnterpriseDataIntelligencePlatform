using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class DatasetModuleTests
{
    private sealed class CurrentUser(Guid? workspaceId, bool platform = false) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? WorkspaceId { get; } = workspaceId;
        public Guid? SessionId { get; } = Guid.NewGuid();
        public bool IsPlatformAdministrator { get; } = platform;
    }

    [Fact]
    public void DatasetStatuses_ContainRequiredLifecycleValues()
    {
        Assert.Contains(DatasetStatuses.Draft, DatasetStatuses.All);
        Assert.Contains(DatasetStatuses.Active, DatasetStatuses.All);
        Assert.Contains(DatasetStatuses.Archived, DatasetStatuses.All);
        Assert.DoesNotContain(DatasetStatuses.SoftDeleted, DatasetStatuses.All);
    }

    [Fact]
    public void DatasetPermissions_ContainRequiredActions()
    {
        Assert.Contains(Permissions.DatasetsView, Permissions.All);
        Assert.Contains(Permissions.DatasetsCreate, Permissions.All);
        Assert.Contains(Permissions.DatasetsUpdate, Permissions.All);
        Assert.Contains(Permissions.DatasetsArchive, Permissions.All);
        Assert.Contains(Permissions.DatasetsRestore, Permissions.All);
        Assert.Contains(Permissions.DatasetsDelete, Permissions.All);
        Assert.Contains(Permissions.DatasetVersionsRestore, Permissions.All);
    }

    [Fact]
    public async Task Dataset_IsStampedWithCurrentWorkspace()
    {
        var workspaceId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options, new CurrentUser(workspaceId));
        var dataset = new Dataset
        {
            Code = "DS-000001",
            Name = "Employee Master",
            NormalizedName = "EMPLOYEE MASTER",
            CategoryId = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            DataSourceName = "HR Database",
            DataSourceType = "SQL Server"
        };

        db.Datasets.Add(dataset);
        await db.SaveChangesAsync();

        Assert.Equal(workspaceId, dataset.WorkspaceId);
        Assert.Equal(1, dataset.CurrentVersion);
        Assert.Equal(DatasetStatuses.Draft, dataset.Status);
    }
}
