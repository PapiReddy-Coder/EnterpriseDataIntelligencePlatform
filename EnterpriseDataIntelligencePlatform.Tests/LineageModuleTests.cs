using System.Text.Json;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Seed;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class LineageModuleTests
{
    [Fact]
    public async Task AutomaticCapture_IsIdempotentAndUsesVersionCurrentAtActivityTime()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Capture.CaptureTransformationAsync(fixture.TransformationId, default);
        await fixture.Capture.CaptureImportAsync(fixture.ImportId, default);
        await fixture.Capture.CaptureQualityProfileAsync(fixture.ProfileId, default);
        await fixture.Capture.CaptureImportAsync(fixture.ImportId, default);

        var edges = await fixture.Db.LineageRelationships.AsNoTracking().ToListAsync();
        Assert.All(edges, x => Assert.True(x.IsAutomatic));
        Assert.All(edges.Where(x => x.ProcessEntityId is not null), x =>
        {
            Assert.Equal(fixture.Version1Id, x.RelevantDatasetVersionId);
            Assert.Equal(1, x.RelevantDatasetVersionNumber);
        });
        Assert.Single(edges.Where(x => x.RelationshipType == LineageRelationshipTypes.ProvidesData));
        Assert.Single(edges.Where(x => x.RelationshipType == LineageRelationshipTypes.Loads));
        Assert.Single(edges.Where(x => x.RelationshipType == LineageRelationshipTypes.Configures));
        Assert.Single(edges.Where(x => x.RelationshipType == LineageRelationshipTypes.AppliesTransformation));
        Assert.Single(edges.Where(x => x.RelationshipType == LineageRelationshipTypes.Profiles));
        Assert.Contains(edges, x => x.SourceEntityType == LineageEntityTypes.DataSource &&
                                    x.TargetEntityId == fixture.Version1Id);
    }

    [Fact]
    public async Task ExistingRowsAreNotBackfilledUntilAnActivityInvokesCapture()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        Assert.Empty(await fixture.Db.LineageRelationships.ToListAsync());
        await fixture.Capture.CaptureDatasetVersionAsync(fixture.Version2Id, default);
        Assert.Equal(2, await fixture.Db.LineageRelationships.CountAsync());
    }

    [Fact]
    public async Task ManualRelationship_CanBeUpdatedAndIsSoftDeactivatedWithAuditHistory()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default);
        Assert.False(created.IsAutomatic);

        var duplicate = await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default));
        Assert.Equal("DuplicateLineage", duplicate.Code);

        var updated = await fixture.Service.UpdateAsync(created.Id,
            new UpdateLineageRelationshipRequest(Json("{\"reason\":\"curated dependency\"}")), default);
        Assert.Equal("curated dependency", updated.Metadata["reason"]?.ToString());

        var inactive = await fixture.Service.DeactivateAsync(created.Id,
            new DeactivateLineageRelationshipRequest("Dependency was superseded"), default);
        Assert.False(inactive.IsActive);
        Assert.Equal("Dependency was superseded", inactive.DeactivationReason);
        Assert.NotNull(await fixture.Db.LineageRelationships.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == created.Id));
        Assert.Empty((await fixture.Service.SearchAsync(new(), default)).Items);
        Assert.Single((await fixture.Service.SearchAsync(new() { IsActive = null }, default)).Items);
        Assert.Contains(fixture.Audit.Events, x => x.Action == "Lineage Relationship Created");
        Assert.Contains(fixture.Audit.Events, x => x.Action == "Lineage Relationship Updated");
        Assert.Contains(fixture.Audit.Events, x => x.Action == "Lineage Relationship Removed");
    }

    [Fact]
    public async Task AutomaticRelationshipsRejectMetadataUpdatesButAllowHistoricalDeactivation()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Capture.CaptureDatasetVersionAsync(fixture.Version1Id, default);
        var edge = await fixture.Db.LineageRelationships.FirstAsync();
        var error = await Assert.ThrowsAsync<AnalyticsRequestException>(() => fixture.Service.UpdateAsync(edge.Id,
            new UpdateLineageRelationshipRequest(Json("{}")), default));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("ImmutableAutomaticLineage", error.Code);
        var result = await fixture.Service.DeactivateAsync(edge.Id,
            new DeactivateLineageRelationshipRequest("Incorrect source metadata"), default);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task IntegrityValidationRejectsSelfReferencesCyclesMissingEntitiesAndCrossWorkspaceEdges()
    {
        await using var fixture = await LineageFixture.CreateAsync(platform: true);
        Assert.Equal("SelfReference", (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetAId), default))).Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.CreateAsync(Request(Guid.NewGuid(), fixture.DatasetAId), default))).StatusCode);
        Assert.Equal("CrossWorkspaceLineage", (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.ForeignDatasetId), default))).Code);

        await fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default);
        await fixture.Service.CreateAsync(Request(fixture.DatasetBId, fixture.DatasetCId), default);
        var cycle = await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.CreateAsync(Request(fixture.DatasetCId, fixture.DatasetAId), default));
        Assert.Equal("CircularDependency", cycle.Code);
    }

    [Fact]
    public async Task UpstreamDownstreamCompleteAndImpactTraversalReturnGraphReadyNodesAndEdges()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default);
        await fixture.Service.CreateAsync(Request(fixture.DatasetBId, fixture.DatasetCId), default);

        var downstream = await fixture.Service.DatasetAsync(fixture.DatasetAId, LineageDirections.Downstream, new(), default);
        Assert.Equal(3, downstream.Nodes.Count);
        Assert.Equal(2, downstream.Relationships.Count);
        Assert.False(downstream.Truncated);
        Assert.All(downstream.Relationships, x => Assert.Equal("SourceToTarget", x.Direction));

        var upstream = await fixture.Service.DatasetAsync(fixture.DatasetCId, LineageDirections.Upstream, new(), default);
        Assert.Equal(3, upstream.Nodes.Count);
        var complete = await fixture.Service.DatasetAsync(fixture.DatasetBId, LineageDirections.Complete, new(), default);
        Assert.Equal(3, complete.Nodes.Count);

        var limited = await fixture.Service.ImpactAsync(fixture.DatasetAId, new() { Depth = 1 }, default);
        Assert.Equal(1, limited.AffectedDatasetCount);
        Assert.True(limited.Truncated);
        var impact = await fixture.Service.ImpactAsync(fixture.DatasetAId, new(), default);
        Assert.Equal(2, impact.AffectedDatasetCount);
        Assert.False(impact.Truncated);
        Assert.Contains(fixture.Audit.Events, x => x.Action == "Impact Analysis Requested");
    }

    [Fact]
    public async Task VersionImportAndSourceTraversalAreAvailable()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Capture.CaptureImportAsync(fixture.ImportId, default);
        await fixture.Capture.CaptureQualityProfileAsync(fixture.ProfileId, default);

        var version = await fixture.Service.DatasetVersionAsync(fixture.DatasetAId, 1, new(), default);
        Assert.Contains(version.Nodes, x => x.EntityType == LineageEntityTypes.Import);
        Assert.Contains(version.Nodes, x => x.EntityType == LineageEntityTypes.QualityProfile);
        var import = await fixture.Service.ImportAsync(fixture.ImportId, new(), default);
        Assert.Contains(import.Nodes, x => x.EntityType == LineageEntityTypes.SourceFile);
        Assert.Contains(import.Nodes, x => x.EntityType == LineageEntityTypes.DatasetVersion);
        var sourceNode = version.Nodes.Single(x => x.EntityType == LineageEntityTypes.DataSource);
        var source = await fixture.Service.SourceAsync(LineageEntityTypes.DataSource, sourceNode.EntityId, new(), default);
        Assert.Contains(source.Nodes, x => x.EntityType == LineageEntityTypes.DatasetVersion);
        Assert.Equal(404, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.DatasetVersionAsync(fixture.DatasetBId, 99, new(), default))).StatusCode);
    }

    [Fact]
    public async Task SearchSupportsFiltersDatesSortingPaginationEmptyResultsAndInvalidParameters()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default);
        await fixture.Service.CreateAsync(Request(fixture.DatasetBId, fixture.DatasetCId), default);
        var page = await fixture.Service.SearchAsync(new()
        {
            RelationshipType = "feeds", Page = 2, PageSize = 1,
            SortBy = "targetName", SortDirection = "asc", FromUtc = DateTime.UtcNow.AddMinutes(-5)
        }, default);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Pagination.TotalPages);
        Assert.Empty((await fixture.Service.SearchAsync(new() { Search = "not-present" }, default)).Items);
        Assert.Equal(400, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.SearchAsync(new() { PageSize = 101 }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.SearchAsync(new() { FromUtc = DateTime.UtcNow, ToUtc = DateTime.UtcNow.AddDays(-1) }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.DatasetAsync(fixture.DatasetAId, LineageDirections.Downstream, new() { Depth = 11 }, default))).StatusCode);
    }

    [Fact]
    public async Task WorkspaceIsolationHidesForeignLineage()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        await fixture.Service.CreateAsync(Request(fixture.DatasetAId, fixture.DatasetBId), default);
        LineageGraphResponse? leaked = null;
        var error = await Record.ExceptionAsync(async () =>
            leaked = await fixture.Service.DatasetAsync(fixture.ForeignDatasetId, LineageDirections.Complete, new(), default));
        Assert.True(error is AnalyticsRequestException { StatusCode: 404 },
            $"Foreign dataset access returned {error?.GetType().Name ?? "success"}; " +
            $"current={fixture.User.WorkspaceId}; foreign={fixture.ForeignWorkspaceId}; " +
            $"root={leaked?.Root.WorkspaceId}; rootId={leaked?.Root.EntityId}; requested={fixture.ForeignDatasetId}; " +
            $"datasetA={fixture.DatasetAId}; platform={fixture.User.IsPlatformAdministrator}.");
        Assert.All((await fixture.Service.SearchAsync(new() { IsActive = null }, default)).Items,
            x => Assert.Equal(fixture.WorkspaceId, x.WorkspaceId));
    }

    [Fact]
    public async Task WorkspaceIsolationRejectsForeignWorkspaceFilter()
    {
        await using var fixture = await LineageFixture.CreateAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Service.SearchAsync(new() { WorkspaceId = fixture.ForeignWorkspaceId }, default))).StatusCode);
    }

    [Fact]
    public void RoleSeedMatchesTheApprovedLineageAccessMatrix()
    {
        var permissionIds = PermissionSeed.Data.ToDictionary(x => x.Name, x => x.Id);
        var roleIds = RoleSeed.Data.ToDictionary(x => x.Name!, x => x.Id);
        bool Has(string role, string permission) => RolePermissionSeed.Data.Any(x =>
            x.RoleId == roleIds[role] && x.PermissionId == permissionIds[permission]);

        foreach (var role in new[] { Roles.PlatformAdministrator, Roles.WorkspaceAdministrator,
                     Roles.DataAnalyst, Roles.BusinessUser, Roles.Viewer })
            Assert.True(Has(role, Permissions.LineageView));
        Assert.True(Has(Roles.PlatformAdministrator, Permissions.LineageManage));
        Assert.True(Has(Roles.WorkspaceAdministrator, Permissions.LineageManage));
        Assert.False(Has(Roles.DataAnalyst, Permissions.LineageManage));
        Assert.True(Has(Roles.DataAnalyst, Permissions.LineageImpact));
        Assert.False(Has(Roles.BusinessUser, Permissions.LineageImpact));
        Assert.False(Has(Roles.Viewer, Permissions.LineageImpact));
    }

    private static CreateLineageRelationshipRequest Request(Guid source, Guid target) =>
        new(LineageEntityTypes.Dataset, source, LineageEntityTypes.Dataset, target, LineageRelationshipTypes.Feeds, Json("{}"));

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
}

internal sealed class LineageFixture : IAsyncDisposable
{
    public Guid WorkspaceId { get; } = Guid.NewGuid();
    public Guid ForeignWorkspaceId { get; } = Guid.NewGuid();
    public Guid DatasetAId { get; } = Guid.NewGuid();
    public Guid DatasetBId { get; } = Guid.NewGuid();
    public Guid DatasetCId { get; } = Guid.NewGuid();
    public Guid ForeignDatasetId { get; } = Guid.NewGuid();
    public Guid Version1Id { get; } = Guid.NewGuid();
    public Guid Version2Id { get; } = Guid.NewGuid();
    public Guid TransformationId { get; } = Guid.NewGuid();
    public Guid ImportId { get; } = Guid.NewGuid();
    public Guid ProfileId { get; } = Guid.NewGuid();
    public AppDbContext Db { get; private set; } = null!;
    public AnalyticsUser User { get; private set; } = null!;
    public LineageCaptureService Capture { get; private set; } = null!;
    public LineageService Service { get; private set; } = null!;
    public AnalyticsAudit Audit { get; } = new();

    public static async Task<LineageFixture> CreateAsync(bool platform = false)
    {
        var f = new LineageFixture();
        var userId = Guid.NewGuid();
        var foreignOwnerId = Guid.NewGuid();
        var user = new AnalyticsUser(userId, f.WorkspaceId, platform);
        f.User = user;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seedDb = new AppDbContext(options, new AnalyticsUser(userId, f.WorkspaceId, true));
        await seedDb.Database.EnsureCreatedAsync();
        seedDb.Workspaces.AddRange(
            new Workspace { Id = f.WorkspaceId, Code = "LOCAL", Name = "Local workspace" },
            new Workspace { Id = f.ForeignWorkspaceId, Code = "OTHER", Name = "Foreign workspace" });
        seedDb.Users.AddRange(
            new AppUser { Id = userId, UserName = "local", FullName = "Local Owner", WorkspaceId = f.WorkspaceId },
            new AppUser { Id = foreignOwnerId, UserName = "foreign", FullName = "Foreign Owner", WorkspaceId = f.ForeignWorkspaceId });
        var category = new DatasetCategory { Id = Guid.NewGuid(), Name = "Test lineage", NormalizedName = "TEST LINEAGE" };
        seedDb.DatasetCategories.Add(category);
        var epoch = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        seedDb.Datasets.AddRange(
            Dataset(f.DatasetAId, f.WorkspaceId, userId, category.Id, "A", epoch),
            Dataset(f.DatasetBId, f.WorkspaceId, userId, category.Id, "B", epoch),
            Dataset(f.DatasetCId, f.WorkspaceId, userId, category.Id, "C", epoch),
            Dataset(f.ForeignDatasetId, f.ForeignWorkspaceId, foreignOwnerId, category.Id, "X", epoch));
        seedDb.DatasetVersions.AddRange(
            Version(f.Version1Id, f.DatasetAId, f.WorkspaceId, userId, category.Id, "A", 1, false, epoch),
            Version(f.Version2Id, f.DatasetAId, f.WorkspaceId, userId, category.Id, "A", 2, true, epoch.AddDays(2)),
            Version(Guid.NewGuid(), f.DatasetBId, f.WorkspaceId, userId, category.Id, "B", 1, true, epoch),
            Version(Guid.NewGuid(), f.DatasetCId, f.WorkspaceId, userId, category.Id, "C", 1, true, epoch),
            Version(Guid.NewGuid(), f.ForeignDatasetId, f.ForeignWorkspaceId, foreignOwnerId, category.Id, "X", 1, true, epoch));
        var file = new UploadedDataFile
        {
            Id = Guid.NewGuid(), DatasetId = f.DatasetAId, WorkspaceId = f.WorkspaceId, UploadedByUserId = userId,
            OriginalFileName = "orders.csv", StoredFileName = "stored.csv", FilePath = "App_Data/orders.csv",
            Extension = ".csv", FileSizeBytes = 123, UploadedAtUtc = epoch.AddDays(1)
        };
        seedDb.UploadedDataFiles.Add(file);
        seedDb.DatasetTransformationConfigurations.Add(new DatasetTransformationConfiguration
        {
            Id = f.TransformationId, DatasetId = f.DatasetAId, WorkspaceId = f.WorkspaceId, Version = 3,
            IsActive = true, CreatedByUserId = userId, CreatedAtUtc = epoch.AddDays(1), ConfigurationJson = "{}"
        });
        seedDb.DataImports.Add(new DataImport
        {
            Id = f.ImportId, DatasetId = f.DatasetAId, WorkspaceId = f.WorkspaceId, FileId = file.Id,
            InitiatedByUserId = userId, CreatedAtUtc = epoch.AddDays(1),
            TransformationConfigurationId = f.TransformationId, TransformationConfigurationVersion = 3
        });
        seedDb.DataQualityProfileRuns.Add(new DataQualityProfileRun
        {
            Id = f.ProfileId, DatasetId = f.DatasetAId, WorkspaceId = f.WorkspaceId,
            ImportId = f.ImportId, RequestedByUserId = userId, CreatedAtUtc = epoch.AddDays(1), QueuedAtUtc = epoch.AddDays(1)
        });
        await seedDb.SaveChangesAsync();
        f.Db = new AppDbContext(options, user);
        f.Capture = new(f.Db);
        f.Service = new(f.Db, user, f.Audit, Options.Create(new LineageOptions
        {
            DefaultTraversalDepth = 5, MaxTraversalDepth = 10, MaxGraphNodes = 1000
        }));
        return f;
    }

    private static Dataset Dataset(Guid id, Guid workspaceId, Guid ownerId, Guid categoryId, string code, DateTime created) => new()
    {
        Id = id, WorkspaceId = workspaceId, OwnerId = ownerId, CategoryId = categoryId,
        Code = code, Name = $"Dataset {code}", NormalizedName = $"DATASET {code}",
        DataSourceName = $"Source {code}", DataSourceType = "File", Status = DatasetStatuses.Active,
        CurrentVersion = code == "A" ? 2 : 1, CreatedAtUtc = created, UpdatedAtUtc = created
    };

    private static DatasetVersion Version(Guid id, Guid datasetId, Guid workspaceId, Guid ownerId,
        Guid categoryId, string code, int number, bool current, DateTime created) => new()
    {
        Id = id, DatasetId = datasetId, WorkspaceId = workspaceId, OwnerId = ownerId, CategoryId = categoryId,
        Code = code, Name = $"Dataset {code}", CategoryName = "Test lineage", OwnerName = "Owner",
        DataSourceName = $"Source {code}", DataSourceType = "File", Status = DatasetStatuses.Active,
        VersionNumber = number, IsCurrent = current, CreatedByUserId = ownerId, CreatedAtUtc = created
    };

    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}
