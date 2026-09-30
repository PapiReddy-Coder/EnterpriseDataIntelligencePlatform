using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TASK20_SQLSERVER_TEST_CONNECTION")))
            Skip = "Set TASK20_SQLSERVER_TEST_CONNECTION to a test SQL Server account allowed to create a disposable database.";
    }
}

public sealed class AnalyticsSqlServerTests
{
    [SqlServerFact]
    public async Task RealViews_LatestProfilesSoftDeletesScopesAttentionAndThresholdSnapshots()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var first = fixture.Datasets[0];
        // Older historical runs must not get additional weight in the workspace average.
        fixture.Db.DataQualityProfileRuns.Add(fixture.Profile(first, 10, SqlFixture.Start.AddMinutes(1)));
        fixture.Db.DataQualityThresholds.Add(new DataQualityThreshold
        {
            DatasetId = first.Id, WorkspaceId = first.WorkspaceId, MinimumOverallScore = 99,
            MinimumCompleteness = 99, MinimumValidity = 99, MinimumUniqueness = 99, MinimumConsistency = 99
        });
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Service.DashboardAsync(new() { WorkspaceId = fixture.WorkspaceId }, default);
        Assert.Equal(4, result.TotalDatasets);
        Assert.Equal(90.67m, result.AverageDatasetQualityScore);
        Assert.Equal(1, result.DatasetsRequiringAttention);
        var current = await fixture.Db.DatasetAnalytics.SingleAsync(x => x.DatasetId == first.Id);
        Assert.Equal(80m, current.AppliedOverallThreshold);
        Assert.Equal("Passed", current.ThresholdStatus);
        Assert.DoesNotContain(await fixture.Db.DatasetAnalytics.ToListAsync(), x => x.Name == "Deleted");

        var failedImport = await fixture.AddImportAsync(first, ImportStatuses.Failed, SqlFixture.Start.AddDays(3));
        var attention = await fixture.Service.AttentionAsync(new() { WorkspaceId = fixture.WorkspaceId }, default);
        Assert.Contains(attention.Items.Single(x => x.DatasetId == first.Id).Reasons, x => x == "LatestImportFailed");
        failedImport.Status = ImportStatuses.CompletedWithErrors;
        failedImport.SuccessfullyImportedRecords = 8; failedImport.RejectedRecords = 2;
        await fixture.Db.SaveChangesAsync();
        current = await fixture.Db.DatasetAnalytics.AsNoTracking().SingleAsync(x => x.DatasetId == first.Id);
        Assert.True(current.NoProfileAfterLatestImport);
        Assert.True(current.LatestImportNeedsAttention);
        fixture.Db.DataQualityIssues.Add(new DataQualityIssue
        {
            ProfileRunId = current.LatestProfileRunId!.Value, DatasetId = first.Id, WorkspaceId = first.WorkspaceId,
            IssueType = "Validation Failure", Severity = "Error", InvalidValue = "do-not-export", Description = "Example"
        });
        await fixture.Db.SaveChangesAsync();
        var issues = await fixture.Service.IssuesAsync(new() { WorkspaceId = fixture.WorkspaceId }, null, null, default);
        Assert.Equal(1, issues.Items.Single().IssueCount);
        Assert.True((await fixture.Db.DatasetAnalytics.AsNoTracking().SingleAsync(x => x.DatasetId == first.Id)).SignificantQualityIssues);
        var summary = await fixture.Service.ImportSummaryAsync(new() { WorkspaceId = fixture.WorkspaceId }, default);
        Assert.Equal(0, summary.SuccessfulImports);
        Assert.Equal(1, summary.CompletedWithErrors);
        Assert.Equal(0m, summary.ImportSuccessRate);
        Assert.Equal(10, summary.TotalRecordsProcessed);
        Assert.Equal(80m, summary.RecordSuccessRate);
    }

    [SqlServerFact]
    public async Task RealTrendQueries_DeduplicatePerDatasetAndBucket_AndRespectPartialRanges()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var a = fixture.Datasets[0];
        fixture.Db.DataQualityProfileRuns.Add(fixture.Profile(a, 10, SqlFixture.Start.AddMinutes(1)));
        // A later run outside the filter must not suppress a qualifying earlier run in the same bucket.
        fixture.Db.DataQualityProfileRuns.Add(fixture.Profile(a, 40, SqlFixture.Start.AddHours(20)));
        await fixture.Db.SaveChangesAsync();
        foreach (var grouping in new[] { "daily", "weekly", "monthly" })
        {
            var points = await fixture.Service.QualityTrendAsync(new TrendQuery
            {
                WorkspaceId = fixture.WorkspaceId, Grouping = grouping, FromUtc = SqlFixture.Start,
                ToUtc = SqlFixture.Start.AddHours(12)
            }, default);
            var point = Assert.Single(points.Items);
            Assert.Equal(3, point.DatasetCount);
            Assert.Equal(90.67m, point.OverallQualityScore);
        }
        await fixture.AddImportAsync(a, ImportStatuses.Completed, SqlFixture.Start.AddHours(3));
        await fixture.AddImportAsync(a, ImportStatuses.CompletedWithErrors, SqlFixture.Start.AddHours(4));
        await fixture.AddImportAsync(a, ImportStatuses.Failed, SqlFixture.Start.AddHours(5));
        await fixture.AddImportAsync(a, ImportStatuses.Cancelled, SqlFixture.Start.AddHours(6));
        foreach (var grouping in new[] { "daily", "weekly", "monthly" })
        {
            var points = await fixture.Service.ImportTrendAsync(new TrendQuery
            {
                WorkspaceId = fixture.WorkspaceId, Grouping = grouping, FromUtc = SqlFixture.Start,
                ToUtc = SqlFixture.Start.AddDays(1)
            }, default);
            var point = Assert.Single(points.Items);
            Assert.Equal(4, point.Imports.TotalImports);
            Assert.Equal(33.33m, point.Imports.ImportSuccessRate);
        }
    }

    [SqlServerFact]
    public async Task RealReportQueriesAndRepeatableUpgrade_RunForAllFiveReports()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await fixture.AddImportAsync(fixture.Datasets[0], ImportStatuses.Completed, SqlFixture.Start);
        foreach (var batch in AnalyticsSchema.UpgradeBatches()) await fixture.Db.Database.ExecuteSqlRawAsync(batch);
        var query = new AnalyticsQuery { WorkspaceId = fixture.WorkspaceId };
        foreach (var type in ReportService.ReportTypes)
        {
            var report = await fixture.Reports.GenerateAsync(type, query, true, default);
            Assert.NotEmpty(report.Data.Rows);
            Assert.NotEmpty(new ReportExporter().Export(report.Data, "xlsx", default).Bytes);
        }
        var user = new AnalyticsUser(Guid.NewGuid(), fixture.WorkspaceId);
        await using var scoped = new AppDbContext(fixture.Options, user);
        Assert.All(await scoped.DatasetAnalytics.ToListAsync(), x => Assert.Equal(fixture.WorkspaceId, x.WorkspaceId));
        Assert.All(await scoped.ImportAnalytics.ToListAsync(), x => Assert.Equal(fixture.WorkspaceId, x.WorkspaceId));
        var permission = await fixture.Db.Permissions.SingleAsync(x => x.Name == Permissions.ReportsExport);
        Assert.Equal(3, await fixture.Db.RolePermissions.CountAsync(x => x.PermissionId == permission.Id));
    }

    [SqlServerFact]
    public async Task RealLineageTableIndexesPermissionsAndFilteredUniquenessAreApplied()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        var connection = (SqlConnection)fixture.Db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.LineageRelationships') AND name LIKE N'IX_LineageRelationships%'";
            Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 9);
        }
        Assert.Equal(3, await fixture.Db.Permissions.CountAsync(x => x.Name.StartsWith("lineage.")));

        var source = fixture.Datasets[0];
        var target = fixture.Datasets[1];
        LineageRelationship Edge() => new()
        {
            WorkspaceId = fixture.WorkspaceId, DatasetId = target.Id, OwnerId = target.OwnerId,
            SourceEntityType = LineageEntityTypes.Dataset, SourceEntityId = source.Id, SourceDatasetId = source.Id,
            SourceDisplayName = source.Name, TargetEntityType = LineageEntityTypes.Dataset,
            TargetEntityId = target.Id, TargetDatasetId = target.Id, TargetDisplayName = target.Name,
            RelationshipType = LineageRelationshipTypes.Feeds, MetadataJson = "{}", IsActive = true
        };
        fixture.Db.LineageRelationships.Add(Edge());
        await fixture.Db.SaveChangesAsync();
        fixture.Db.LineageRelationships.Add(Edge());
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    private sealed class SqlFixture : IAsyncDisposable
    {
        public static readonly DateTime Start = new(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc);
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; private set; } = null!;
        public DbContextOptions<AppDbContext> Options { get; private set; } = null!;
        public List<Dataset> Datasets { get; } = [];
        public AnalyticsService Service { get; private set; } = null!;
        public ReportService Reports { get; private set; } = null!;
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        private bool created;

        public static async Task<SqlFixture> CreateAsync()
        {
            var fixture = new SqlFixture();
            var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TASK20_SQLSERVER_TEST_CONNECTION"))
            {
                InitialCatalog = "Task20Tests_" + Guid.NewGuid().ToString("N")
            };
            fixture.Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
            var user = new AnalyticsUser(Guid.NewGuid(), null, true);
            fixture.Db = new(fixture.Options, user);
            try
            {
                fixture.created = await fixture.Db.Database.EnsureCreatedAsync();
                foreach (var batch in AnalyticsSchema.UpgradeBatches()) await fixture.Db.Database.ExecuteSqlRawAsync(batch);
                var other = Guid.NewGuid();
                fixture.Db.Workspaces.AddRange(new Workspace { Id = fixture.WorkspaceId, Code = "SQL-A", Name = "SQL A" },
                    new Workspace { Id = other, Code = "SQL-B", Name = "SQL B" });
                var owner = new AppUser { Id = Guid.NewGuid(), FullName = "SQL Owner", WorkspaceId = fixture.WorkspaceId };
                fixture.Db.Users.Add(owner);
                var categoryId = await fixture.Db.DatasetCategories.Select(x => x.Id).FirstAsync();
                var scores = new decimal?[] { 92, 86, 94, null, 0, 0 };
                for (var i = 0; i < scores.Length; i++)
                {
                    var dataset = new Dataset
                    {
                        Id = Guid.NewGuid(), Name = i == 4 ? "Deleted" : $"Dataset-{i}", NormalizedName = $"DATASET-{i}",
                        Code = $"SQL-{i}", WorkspaceId = i == 5 ? other : fixture.WorkspaceId, CategoryId = categoryId, OwnerId = owner.Id,
                        DataSourceName = "Test", DataSourceType = "CSV", Status = DatasetStatuses.Active, IsDeleted = i == 4
                    };
                    fixture.Datasets.Add(dataset); fixture.Db.Datasets.Add(dataset);
                    if (scores[i].HasValue) fixture.Db.DataQualityProfileRuns.Add(fixture.Profile(dataset, scores[i]!.Value, Start.AddHours(2)));
                }
                await fixture.Db.SaveChangesAsync();
                var query = new AnalyticsQueries(fixture.Db, user);
                var options = Microsoft.Extensions.Options.Options.Create(new AnalyticsOptions());
                fixture.Service = new(fixture.Db, query, user, fixture.cache, options);
                fixture.Reports = new(query, fixture.Service, new AnalyticsAudit(), options);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public DataQualityProfileRun Profile(Dataset dataset, decimal score, DateTime completed) => new()
        {
            DatasetId = dataset.Id, WorkspaceId = dataset.WorkspaceId, Status = QualityProfileStatuses.Completed,
            TotalRecords = 10, OverallQualityScore = score, CompletenessScore = score, ValidityScore = score,
            UniquenessScore = score, ConsistencyScore = score, AppliedOverallThreshold = 80,
            AppliedCompletenessThreshold = 80, AppliedValidityThreshold = 80, AppliedUniquenessThreshold = 80,
            AppliedConsistencyThreshold = 80, ThresholdStatus = "Passed", CompletedAtUtc = completed, CreatedAtUtc = completed.AddMinutes(-1)
        };

        public async Task<DataImport> AddImportAsync(Dataset dataset, string status, DateTime createdAt)
        {
            var file = new UploadedDataFile
            {
                DatasetId = dataset.Id, WorkspaceId = dataset.WorkspaceId, OriginalFileName = "test.csv",
                StoredFileName = "test.csv", FilePath = "unused-test-path", Extension = ".csv"
            };
            Db.UploadedDataFiles.Add(file);
            var import = new DataImport
            {
                DatasetId = dataset.Id, WorkspaceId = dataset.WorkspaceId, FileId = file.Id, Status = status,
                InitiatedByUserId = dataset.OwnerId, TotalRecords = 10,
                SuccessfullyImportedRecords = status == ImportStatuses.Completed ? 10 : status == ImportStatuses.CompletedWithErrors ? 8 : 0,
                RejectedRecords = status == ImportStatuses.CompletedWithErrors ? 2 : 0,
                CreatedAtUtc = createdAt, StartedAtUtc = createdAt, CompletedAtUtc = createdAt.AddMinutes(2)
            };
            Db.DataImports.Add(import); await Db.SaveChangesAsync(); return import;
        }

        public async ValueTask DisposeAsync()
        {
            // Only the unique database created by this fixture is removed, never the connection's original database.
            if (created) await Db.Database.EnsureDeletedAsync();
            if (Db is not null) await Db.DisposeAsync();
            cache.Dispose();
        }
    }
}
