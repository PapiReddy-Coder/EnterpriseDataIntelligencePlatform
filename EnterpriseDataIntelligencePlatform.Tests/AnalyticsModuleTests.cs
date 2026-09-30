using System.Security.Claims;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Analytics;
using EnterpriseDataIntelligencePlatform.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class AnalyticsModuleTests
{
    [Fact]
    public void ImportRate_OnlyCompletedIsSuccessful_AndCancelledIsExcluded()
    {
        var result = AnalyticsRules.Finish(new ImportSummary
        {
            SuccessfulImports = 2, CompletedWithErrors = 1, FailedImports = 1, CancelledImports = 8,
            RecordsSuccessfullyImported = 90, RecordsRejected = 10, TotalRecordsProcessed = 100
        });
        Assert.Equal(50m, result.ImportSuccessRate);
        Assert.Equal(90m, result.RecordSuccessRate);
        Assert.Null(AnalyticsRules.Finish(new ImportSummary()).ImportSuccessRate);
        Assert.Null(AnalyticsRules.Finish(new ImportSummary()).RecordSuccessRate);
    }

    [Theory]
    [InlineData(2026, 8, 31, "daily", "2026-08-31")]
    [InlineData(2026, 8, 30, "weekly", "2026-08-24")]
    [InlineData(2026, 8, 31, "weekly", "2026-08-31")]
    [InlineData(2026, 8, 31, "monthly", "2026-08-01")]
    [InlineData(2027, 1, 1, "weekly", "2026-12-28")]
    public void Bucket_UsesUtcMondayWeeks(int year, int month, int day, string grouping, string expected) =>
        Assert.Equal(expected, AnalyticsRules.Bucket(new DateTime(year, month, day), grouping).ToString("yyyy-MM-dd"));

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void InvalidPagesAreRejected(int page, int size) =>
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { Page = page, PageSize = size }));

    [Fact]
    public void InvalidFiltersAndDateRangesAreRejected()
    {
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { Status = "Deleted" }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { ImportStatus = "done" }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { QualityStatus = "Good" }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { WorkspaceId = Guid.Empty }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { SortDirection = "sideways" }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.Validate(new() { FromUtc = DateTime.Today, ToUtc = DateTime.Today }));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.ValidateSort(new() { SortBy = "SqlCommand" }, "name"));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.TrendRange(
            new() { FromUtc = new(2020, 1, 1), ToUtc = new(2026, 1, 1) }, 366, DateTime.UtcNow));
        Assert.Throws<AnalyticsRequestException>(() => AnalyticsRules.TrendRange(new() { Grouping = "hourly" }, 366, DateTime.UtcNow));
    }

    [Fact]
    public void AttentionReasons_ReturnEveryApplicableReasonOnce()
    {
        var row = new DatasetAnalyticsRow { NoCompletedProfile = true, FailedQualityThreshold = true,
            LatestImportStatus = ImportStatuses.CompletedWithErrors, SignificantQualityIssues = true };
        Assert.Equal(new[] { "NoCompletedProfile", "QualityThresholdFailed", "LatestImportCompletedWithErrors", "SignificantQualityIssues" },
            AnalyticsRules.AttentionReasons(row));
        Assert.Empty(AnalyticsRules.AttentionReasons(new()));
    }

    [Fact]
    public async Task QualitySummary_UsesDatasetRowsAndExcludesUnscoredDatasets()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var result = await fixture.Service.QualitySummaryAsync(new(), default);
        Assert.Equal(4, result.TotalDatasets);
        Assert.Equal(3, result.ScoredDatasets);
        Assert.Equal(1, result.UnprofiledDatasets);
        Assert.Equal(90.67m, result.OverallQualityScore);
        Assert.Equal(3, result.PassingDatasets);
    }

    [Fact]
    public async Task Dashboard_UsesDistinctAttentionDatasetsAndIgnoresOtherWorkspace()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var result = await fixture.Service.DashboardAsync(new(), default);
        Assert.Equal(4, result.TotalDatasets);
        Assert.Equal(2, result.DatasetsRequiringAttention);
        Assert.Equal(2, result.Imports.SuccessfulImports);
        Assert.Equal(1, result.Imports.CompletedWithErrors);
        Assert.Equal(1, result.Imports.FailedImports);
        Assert.Equal(50m, result.Imports.ImportSuccessRate);
        Assert.Equal(30, result.Imports.TotalRecordsProcessed);
        Assert.Equal(40, result.Imports.RecordsAttempted);
        Assert.Equal(10, result.Imports.RecordsWithoutFinalOutcome);
    }

    [Fact]
    public async Task FilteringSortingAndPagination_AreStableAndScoped()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var result = await fixture.Service.DatasetsAsync(new() { SortBy = "name", SortDirection = "asc", PageSize = 2, Page = 2 }, false, default);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(new[] { "C", "D" }, result.Items.Select(x => x.Name));
        Assert.Equal(2, result.Pagination.TotalPages);
        var filtered = await fixture.Service.DatasetsAsync(new() { Search = "B", CategoryId = fixture.CategoryId, OwnerId = fixture.OwnerId }, false, default);
        Assert.Single(filtered.Items);
        Assert.Equal("B", filtered.Items[0].Name);
        Assert.Empty((await fixture.Service.DatasetsAsync(new() { Page = 10 }, false, default)).Items);
    }

    [Fact]
    public async Task DatesAreInclusiveFromAndExclusiveTo()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var result = await fixture.Service.ImportSummaryAsync(new() { FromUtc = AnalyticsFixture.Epoch,
            ToUtc = AnalyticsFixture.Epoch.AddDays(1) }, default);
        Assert.Equal(1, result.TotalImports);
        var empty = await fixture.Service.ImportSummaryAsync(new() { FromUtc = AnalyticsFixture.Epoch.AddYears(1) }, default);
        Assert.Equal(0, empty.TotalImports);
        Assert.Null(empty.ImportSuccessRate);
        Assert.Null(empty.AverageProcessingTimeMilliseconds);
    }

    [Fact]
    public async Task CrossWorkspaceRequestsAreForbidden_AndForeignDatasetsNotFound()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var forbidden = await Assert.ThrowsAsync<AnalyticsRequestException>(() => fixture.Service.DashboardAsync(
            new() { WorkspaceId = fixture.OtherWorkspaceId }, default));
        Assert.Equal(403, forbidden.StatusCode);
        var missing = await Assert.ThrowsAsync<AnalyticsRequestException>(() => fixture.Service.DatasetsAsync(
            new() { DatasetId = fixture.OtherDatasetId }, false, default));
        Assert.Equal(404, missing.StatusCode);
        Assert.Single((await fixture.Service.WorkspacesAsync(new(), default)).Items);
    }

    [Fact]
    public async Task PlatformAdministratorCanSeeBothWorkspaces()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync(platform: true);
        Assert.Equal(5, (await fixture.Service.DashboardAsync(new(), default)).TotalDatasets);
        Assert.Equal(2, (await fixture.Service.WorkspacesAsync(new(), default)).Items.Count);
        Assert.Equal(1, (await fixture.Service.DashboardAsync(new() { WorkspaceId = fixture.OtherWorkspaceId }, default)).TotalDatasets);
    }

    [Fact]
    public async Task UnassignedNonPlatformUserCannotAccessAnalytics()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var user = new AnalyticsUser(Guid.NewGuid(), null);
        var queries = new AnalyticsQueries(fixture.Db, user);
        var error = await Assert.ThrowsAsync<AnalyticsRequestException>(() => queries.ValidateScopeAsync(new(), default));
        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData(Roles.PlatformAdministrator, true)]
    [InlineData(Roles.WorkspaceAdministrator, true)]
    [InlineData(Roles.DataAnalyst, true)]
    [InlineData(Roles.BusinessUser, false)]
    [InlineData(Roles.Viewer, false)]
    public async Task ExistingRoleMatrix_AllowsReadsButRestrictsExports(string roleName, bool canExport)
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var role = await fixture.Db.Roles.SingleAsync(x => x.Name == roleName);
        fixture.Db.UserRoles.Add(new() { UserId = fixture.User.UserId!.Value, RoleId = role.Id });
        await fixture.Db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, fixture.User.UserId.Value.ToString())], "Test"));
        var handler = new AnalyticsPermissionHandler(fixture.Db);
        var read = new AnalyticsPermissionRequirement(false);
        var readContext = new AuthorizationHandlerContext([read], principal, null);
        await handler.HandleAsync(readContext);
        Assert.True(readContext.HasSucceeded);
        var export = new AnalyticsPermissionRequirement(true);
        var exportContext = new AuthorizationHandlerContext([export], principal, null);
        await handler.HandleAsync(exportContext);
        Assert.Equal(canExport, exportContext.HasSucceeded);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("  +SUM(1,2)")]
    [InlineData("-1+1")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\tformula")]
    [InlineData("\rformula")]
    [InlineData("\0=SUM(1,2)")]
    [InlineData("\uFEFF=SUM(1,2)")]
    public void CsvNeutralizesUntrustedFormulaStrings(string input) =>
        Assert.StartsWith("\"'", ReportExporter.CsvCell(input));

    [Fact]
    public void CsvEscapesQuotesCommasAndNewlines_WithoutChangingNumericValues()
    {
        Assert.Equal("\"a,\"\"b\"\"\nc\"", ReportExporter.CsvCell("a,\"b\"\nc"));
        Assert.Equal("\"-25.5\"", ReportExporter.CsvCell(-25.5m));
        Assert.Equal("\"\"", ReportExporter.CsvCell(null));
    }

    [Theory]
    [InlineData("dataset-summary")]
    [InlineData("data-quality")]
    [InlineData("import-activity")]
    [InlineData("data-processing")]
    [InlineData("workspace-summary")]
    public async Task EveryReport_HasMatchingCsvAndValidExcelAndAudits(string type)
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var report = await fixture.Reports.GenerateAsync(type, new(), true, default);
        Assert.NotEmpty(report.Data.Columns);
        Assert.NotEmpty(report.Data.Rows);
        Assert.All(report.Data.Rows, row => Assert.Equal(report.Data.Columns.Select(x => x.Key), row.Keys));
        var exporter = new ReportExporter();
        var csv = exporter.Export(report.Data, "csv", default);
        var csvText = Encoding.UTF8.GetString(csv.Bytes).TrimStart('\uFEFF');
        Assert.StartsWith(ReportExporter.CsvCell(report.Data.Columns[0].Header), csvText);
        var excel = exporter.Export(report.Data, "xlsx", default);
        using var stream = new MemoryStream(excel.Bytes);
        using var workbook = SpreadsheetDocument.Open(stream, false);
        var errors = new OpenXmlValidator().Validate(workbook).Select(x => x.Description).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Equal(new[] { "Summary", "Details" }, workbook.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(x => x.Name!.Value));
        Assert.DoesNotContain(workbook.WorkbookPart.WorksheetParts.SelectMany(x => x.Worksheet.Descendants<Cell>()), x => x.CellFormula is not null);
        var detailsSheet = workbook.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(x => x.Name == "Details");
        var details = (WorksheetPart)workbook.WorkbookPart.GetPartById(detailsSheet.Id!);
        Assert.Equal(report.Data.Rows.Count + 1, details.Worksheet.Descendants<Row>().Count());
        Assert.Contains(fixture.Audit.Events, x => x.Action == "Report Generated");
    }

    [Fact]
    public async Task ExportLimitRejectsRatherThanTruncates_AndPageScopeRemainsAvailable()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync(exportLimit: 2);
        var error = await Assert.ThrowsAsync<AnalyticsRequestException>(() => fixture.Reports.GenerateAsync("dataset-summary", new(), true, default));
        Assert.Equal(413, error.StatusCode);
        var page = await fixture.Reports.GenerateAsync("dataset-summary", new() { PageSize = 2 }, false, default);
        Assert.Equal(2, page.Data.Rows.Count);
        Assert.Equal(4, page.Pagination.TotalCount);
    }

    [Fact]
    public async Task EmptyReportsHaveHeadersAndZeroRows_AndUnknownReportsFail()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync();
        var report = await fixture.Reports.GenerateAsync("dataset-summary", new() { Search = "not found" }, true, default);
        Assert.Empty(report.Data.Rows);
        Assert.Equal(0, report.Pagination.TotalCount);
        Assert.NotEmpty(new ReportExporter().Export(report.Data, "xlsx", default).Bytes);
        Assert.Equal(400, (await Assert.ThrowsAsync<AnalyticsRequestException>(() =>
            fixture.Reports.GenerateAsync("anything", new(), false, default))).StatusCode);
    }

    [Fact]
    public async Task SummaryCache_IsPartitionedByScopeAndStillChecksWorkspaceAuthorization()
    {
        await using var fixture = await AnalyticsFixture.CreateAsync(platform: true, cacheSeconds: 30);
        var first = await fixture.Service.DashboardAsync(new() { WorkspaceId = fixture.WorkspaceId }, default);
        var second = await fixture.Service.DashboardAsync(new() { WorkspaceId = fixture.OtherWorkspaceId }, default);
        Assert.Equal(4, first.TotalDatasets);
        Assert.Equal(1, second.TotalDatasets);
    }

    [Fact]
    public void MigrationSnapshotMatchesModel_AndIdempotentScriptOnlyAddsAnalyticsObjects()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=TranslationOnly;Integrated Security=true").Options,
            new AnalyticsUser(Guid.NewGuid(), null, true));
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20260824060149_Task19DataQualityProfiling",
            "20260831055235_AnalyticsDashboardReporting", MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("EXEC(N'", sql);
        Assert.Contains("CREATE OR ALTER VIEW dbo.vw_DatasetAnalytics", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM dbo.Datasets", sql);
        Assert.Equal(5, AnalyticsSchema.UpgradeBatches().Count);
    }

    [Fact]
    public void SqlServerQueriesTranslateWithoutMaterializingBusinessEntities()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=TranslationOnly;Integrated Security=true;TrustServerCertificate=true").Options,
            new AnalyticsUser(Guid.NewGuid(), Guid.NewGuid()));
        var query = new AnalyticsQueries(db, new AnalyticsUser(Guid.NewGuid(), Guid.NewGuid()));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AnalyticsService(db, query, new AnalyticsUser(Guid.NewGuid(), Guid.NewGuid()), cache, Options.Create(new AnalyticsOptions()));
        Assert.Contains("AVG", query.DatasetScope(new()).AggregateQuality().ToQueryString());
        Assert.Contains("COUNT_BIG", query.Imports(new()).GroupBy(x => x.DatasetId).AggregateImports().ToQueryString());
        Assert.Contains("ORDER BY", AnalyticsQueries.SortDatasets(query.Datasets(new()), new()).Take(25).ToQueryString());
        Assert.Contains("vw_DatasetAnalytics", service.WorkspaceRows(new()).ToQueryString());
        Assert.Contains("ORDER BY", service.SortedWorkspaces(new()).Take(25).ToQueryString());
        Assert.Contains("GROUP BY", query.Datasets(new()).GroupBy(x => x.Status)
            .Select(g => new DatasetDistribution { Key = g.Key, Label = g.Key, DatasetCount = g.LongCount() })
            .OrderByDescending(x => x.DatasetCount).ThenBy(x => x.Key).Take(25).ToQueryString());
        Assert.Contains("GROUP BY", query.Datasets(new()).GroupBy(x => new { x.CategoryId, x.CategoryName })
            .Select(g => new DatasetDistribution { Key = g.Key.CategoryId.ToString(), Label = g.Key.CategoryName, DatasetCount = g.LongCount() })
            .OrderByDescending(x => x.DatasetCount).ThenBy(x => x.Key).Take(25).ToQueryString());
        Assert.Contains("GROUP BY", db.DataQualityIssues.GroupBy(x => new { x.IssueType, x.Severity })
            .Select(g => new QualityIssueAggregate { IssueType = g.Key.IssueType, Severity = g.Key.Severity,
                IssueCount = g.LongCount(), AffectedDatasets = g.Select(x => x.DatasetId).Distinct().LongCount() })
            .OrderByDescending(x => x.IssueCount).ThenBy(x => x.IssueType).Take(25).ToQueryString());
        foreach (var grouping in new[] { "daily", "weekly", "monthly" })
        {
            var filter = new TrendQuery { Grouping = grouping, FromUtc = AnalyticsFixture.Epoch, ToUtc = AnalyticsFixture.Epoch.AddDays(30) };
            Assert.Contains("GROUP BY", service.ImportTrendQuery(filter).ToQueryString());
            var sql = service.QualityTrendQuery(filter, filter.FromUtc.Value, filter.ToUtc.Value).ToQueryString();
            Assert.Contains("ROW_NUMBER()", sql);
            Assert.Contains("@workspace", sql);
            Assert.Contains("GROUP BY", sql);
        }
    }
}

internal sealed record AnalyticsUser(Guid? UserId, Guid? WorkspaceId, bool IsPlatformAdministrator = false) : ICurrentUser
{
    public Guid? SessionId => null;
}

internal sealed class AnalyticsAudit : IAuditService
{
    public List<(string Action, string? Details)> Events { get; } = [];
    public Task WriteAsync(string action, string entityType, string? entityId = null, string? details = null,
        string? ipAddress = null, Guid? userId = null, Guid? workspaceId = null, CancellationToken cancellationToken = default)
    {
        Events.Add((action, details)); return Task.CompletedTask;
    }
}

internal sealed class AnalyticsFixture : IAsyncDisposable
{
    public static readonly DateTime Epoch = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    public Guid WorkspaceId { get; } = Guid.NewGuid();
    public Guid OtherWorkspaceId { get; } = Guid.NewGuid();
    public Guid OtherDatasetId { get; } = Guid.NewGuid();
    public Guid CategoryId { get; } = Guid.NewGuid();
    public Guid OwnerId { get; } = Guid.NewGuid();
    public AnalyticsUser User { get; private set; } = null!;
    public AppDbContext Db { get; private set; } = null!;
    public AnalyticsService Service { get; private set; } = null!;
    public ReportService Reports { get; private set; } = null!;
    public AnalyticsAudit Audit { get; } = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    public static async Task<AnalyticsFixture> CreateAsync(bool platform = false, int exportLimit = 10000, int cacheSeconds = 0)
    {
        var fixture = new AnalyticsFixture();
        fixture.User = new(Guid.NewGuid(), fixture.WorkspaceId, platform);
        fixture.Db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, fixture.User);
        await fixture.Db.Database.EnsureCreatedAsync();
        fixture.Db.Workspaces.AddRange(new Workspace { Id = fixture.WorkspaceId, Code = "A", Name = "Workspace A" },
            new Workspace { Id = fixture.OtherWorkspaceId, Code = "B", Name = "Workspace B" });
        var rows = new List<DatasetAnalyticsRow>();
        var scores = new decimal?[] { 92, 86, 94, null };
        for (var i = 0; i < 4; i++)
            rows.Add(new DatasetAnalyticsRow { DatasetId = Guid.NewGuid(), WorkspaceId = fixture.WorkspaceId,
                WorkspaceName = "Workspace A", Name = ((char)('A' + i)).ToString(), Code = $"DS-{i}",
                CategoryId = fixture.CategoryId, CategoryName = "Finance", OwnerId = fixture.OwnerId, OwnerName = "Owner",
                Status = DatasetStatuses.Active, CreatedAtUtc = Epoch.AddDays(i), UpdatedAtUtc = Epoch.AddDays(i),
                OverallQualityScore = scores[i], Completeness = scores[i],
                LatestProfileRunId = scores[i].HasValue ? Guid.NewGuid() : null,
                ProfileCompletedAtUtc = scores[i].HasValue ? Epoch.AddDays(i) : null,
                ThresholdStatus = scores[i].HasValue ? "Passed" : null,
                NoCompletedProfile = !scores[i].HasValue, RequiresAttention = i >= 2,
                LatestImportNeedsAttention = i == 2, LatestImportStatus = i == 2 ? ImportStatuses.CompletedWithErrors : ImportStatuses.Completed });
        fixture.Db.DatasetAnalytics.AddRange(rows);
        fixture.Db.DatasetAnalytics.Add(new DatasetAnalyticsRow { DatasetId = fixture.OtherDatasetId,
            WorkspaceId = fixture.OtherWorkspaceId, WorkspaceName = "Workspace B", Name = "Foreign", Code = "OTHER",
            Status = DatasetStatuses.Archived, OverallQualityScore = 10 });
        var statuses = new[] { ImportStatuses.Completed, ImportStatuses.Completed, ImportStatuses.CompletedWithErrors, ImportStatuses.Failed };
        for (var i = 0; i < 4; i++)
            fixture.Db.ImportAnalytics.Add(new ImportAnalyticsRow { ImportId = Guid.NewGuid(), DatasetId = rows[i].DatasetId,
                WorkspaceId = fixture.WorkspaceId, DatasetName = rows[i].Name, Status = statuses[i], ImportMode = "Full",
                RecordsAttempted = 10, RecordsSuccessfullyImported = i < 2 ? 10 : i == 2 ? 8 : 0,
                RecordsRejected = i == 2 ? 2 : 0, RecordsProcessed = i < 3 ? 10 : 0,
                RecordsWithoutFinalOutcome = i == 3 ? 10 : 0, StatisticsComplete = i < 3,
                CreatedAtUtc = Epoch.AddDays(i), ProcessingTimeMilliseconds = 1000 });
        fixture.Db.ImportAnalytics.Add(new ImportAnalyticsRow { ImportId = Guid.NewGuid(), DatasetId = fixture.OtherDatasetId,
            WorkspaceId = fixture.OtherWorkspaceId, DatasetName = "Foreign", Status = ImportStatuses.Completed, RecordsProcessed = 999 });
        await fixture.Db.SaveChangesAsync();
        var queries = new AnalyticsQueries(fixture.Db, fixture.User);
        var options = Options.Create(new AnalyticsOptions { MaxExportRows = exportLimit, SummaryCacheSeconds = cacheSeconds });
        fixture.Service = new(fixture.Db, queries, fixture.User, fixture.cache, options);
        fixture.Reports = new(queries, fixture.Service, fixture.Audit, options);
        return fixture;
    }

    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); cache.Dispose(); }
}
