using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class GovernanceModuleTests
{
    private sealed record TestUser(Guid? UserId, Guid? WorkspaceId, bool IsPlatformAdministrator = false,
        bool IsWorkspaceAdministrator = false) : ICurrentUser
    {
        public Guid? SessionId => Guid.NewGuid();
    }

    [Fact]
    public async Task Internal_dataset_does_not_require_a_grant()
    {
        var (db, datasetId, _, memberId) = await CreateAsync(DataClassifications.Internal);
        var policy = new DatasetAccessPolicy(db, new TestUser(memberId, db.Datasets.IgnoreQueryFilters().Single().WorkspaceId));

        var result = await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, default);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Confidential_dataset_requires_an_explicit_grant()
    {
        var (db, datasetId, _, memberId) = await CreateAsync(DataClassifications.Confidential);
        var policy = new DatasetAccessPolicy(db, new TestUser(memberId, db.Datasets.IgnoreQueryFilters().Single().WorkspaceId));

        var result = await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, default);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Read_grant_does_not_allow_write()
    {
        var (db, datasetId, ownerId, memberId) = await CreateAsync(DataClassifications.Restricted);
        var dataset = db.Datasets.IgnoreQueryFilters().Single();
        var request = new DatasetAccessRequest
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = datasetId, RequestingUserId = memberId,
            RequestedAccessLevel = DatasetAccessLevels.Read, BusinessJustification = "Required for reporting analysis",
            Status = AccessRequestStatuses.Approved
        };
        db.DatasetAccessRequests.Add(request);
        db.DatasetAccessGrants.Add(new DatasetAccessGrant
        {
            WorkspaceId = dataset.WorkspaceId, DatasetId = datasetId, UserId = memberId,
            AccessLevel = DatasetAccessLevels.Read, GrantedByUserId = ownerId,
            SourceAccessRequest = request, Status = AccessGrantStatuses.Active
        });
        await db.SaveChangesAsync();
        var policy = new DatasetAccessPolicy(db, new TestUser(memberId, dataset.WorkspaceId));

        Assert.True((await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, default)).Succeeded);
        Assert.False((await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Write, default)).Succeeded);
    }

    [Fact]
    public async Task Dataset_owner_has_implicit_manage_access()
    {
        var (db, datasetId, ownerId, _) = await CreateAsync(DataClassifications.Restricted);
        var workspaceId = db.Datasets.IgnoreQueryFilters().Single().WorkspaceId;
        var policy = new DatasetAccessPolicy(db, new TestUser(ownerId, workspaceId));

        Assert.True((await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Manage, default)).Succeeded);
    }

    [Fact]
    public async Task Cross_workspace_dataset_is_not_disclosed()
    {
        var (db, datasetId, _, memberId) = await CreateAsync(DataClassifications.Public);
        var policy = new DatasetAccessPolicy(db, new TestUser(memberId, Guid.NewGuid()));

        var result = await policy.AuthorizeAsync(datasetId, DatasetAccessLevels.Read, default);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData("Read", 1)]
    [InlineData("Write", 2)]
    [InlineData("Manage", 3)]
    public void Access_levels_have_expected_hierarchy(string level, int expected) =>
        Assert.Equal(expected, DatasetAccessLevels.Rank(level));

    private static async Task<(AppDbContext Db, Guid DatasetId, Guid OwnerId, Guid MemberId)> CreateAsync(string classification)
    {
        var workspaceId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var current = new TestUser(memberId, workspaceId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, current);
        var workspace = new Workspace { Id = workspaceId, Name = "Finance", Code = "FIN" };
        var owner = new AppUser { Id = ownerId, UserName = "owner@test.local", Email = "owner@test.local", FullName = "Owner", WorkspaceId = workspaceId };
        var member = new AppUser { Id = memberId, UserName = "member@test.local", Email = "member@test.local", FullName = "Member", WorkspaceId = workspaceId };
        var category = new DatasetCategory { Id = Guid.NewGuid(), Name = "Finance", NormalizedName = "FINANCE", Description = "Finance", IsActive = true };
        var dataset = new Dataset
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Workspace = workspace, Code = "DS-TEST",
            Name = "Payments", NormalizedName = "PAYMENTS", CategoryId = category.Id, Category = category,
            OwnerId = ownerId, Owner = owner, DataSourceName = "Test", DataSourceType = "CSV"
        };
        var governance = new DatasetGovernance
        {
            WorkspaceId = workspaceId, DatasetId = dataset.Id, Dataset = dataset,
            Classification = classification, GovernanceStatus = GovernanceStatuses.Draft, UpdatedByUserId = ownerId
        };
        db.AddRange(workspace, owner, member, category, dataset, governance);
        await db.SaveChangesAsync();
        return (db, dataset.Id, ownerId, memberId);
    }
}
