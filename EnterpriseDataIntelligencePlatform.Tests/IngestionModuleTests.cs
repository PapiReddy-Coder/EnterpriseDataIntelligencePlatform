using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Controllers;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class IngestionModuleTests
{
    private sealed class Current(Guid? workspace, bool platform = false) : ICurrentUser
    {
        public Guid? UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Guid? WorkspaceId => workspace;
        public Guid? SessionId => Guid.NewGuid();
        public bool IsPlatformAdministrator => platform;
    }

    [Theory]
    [InlineData("data.csv", 100, null)]
    [InlineData("data.xlsx", 100, null)]
    [InlineData("data.txt", 100, "Only CSV and XLSX files are supported.")]
    [InlineData("data.csv", 0, "The uploaded file is empty.")]
    public void FileValidation_EnforcesSupportedFormatsAndEmptyFiles(string name, long size, string? expected)
        => Assert.Equal(expected, ImportRules.ValidateFile(name, size));

    [Fact]
    public void FileValidation_RejectsFilesOver25Mb()
        => Assert.Equal("Maximum file size is 25 MB.", ImportRules.ValidateFile("data.csv", ImportRules.MaxFileSizeBytes + 1));

    [Theory]
    [InlineData("Created", true)]
    [InlineData("Queued", false)]
    [InlineData("Processing", false)]
    public void ImportStatus_StartTransition_IsControlled(string status, bool expected)
        => Assert.Equal(expected, ImportRules.CanStart(status));

    [Theory]
    [InlineData(0, 0, "Completed")]
    [InlineData(1, 0, "Completed With Errors")]
    [InlineData(0, 1, "Completed With Errors")]
    public void CompletionStatus_ReflectsRowLevelErrors(int errors, int rejected, string expected)
        => Assert.Equal(expected, ImportRules.CompletionStatus(errors, rejected));

    [Fact]
    public void KeyColumnValidation_AcceptsMatchingHeaders()
        => Assert.Null(ImportRules.ValidateKeyColumns(
            ["Employee Name"],
            ["Employee Name", "Status"]));

    [Fact]
    public void KeyColumnValidation_NormalizesWhitespaceCaseAndUtf8Bom()
        => Assert.Null(ImportRules.ValidateKeyColumns(
            [" employee name "],
            ["\uFEFFEmployee Name", "Status"]));

    [Fact]
    public void KeyColumnValidation_ReportsUnknownAndAvailableHeaders()
    {
        var error = ImportRules.ValidateKeyColumns(
            ["Employee Name"],
            ["EmployeeName", "Status"]);

        Assert.Equal(
            "Unknown key column(s): Employee Name. Available columns: EmployeeName, Status.",
            error);
    }

    [Fact]
    public void ImportController_UsesPermissionBasedAuthorization()
    {
        var methods = typeof(ImportsController).GetMethods().Where(m => m.DeclaringType == typeof(ImportsController)).ToArray();
        Assert.Contains(methods, m => m.Name == "Upload" && m.GetCustomAttributes(typeof(HasPermissionAttribute), true).Any());
        Assert.Contains(methods, m => m.Name == "Start" && m.GetCustomAttributes(typeof(HasPermissionAttribute), true).Any());
        Assert.Contains(methods, m => m.Name == "Get" && m.GetCustomAttributes(typeof(HasPermissionAttribute), true).Any());
        Assert.Contains(methods, m => m.Name == "Cancel" && m.GetCustomAttributes(typeof(HasPermissionAttribute), true).Any());
    }

    [Fact]
    public async Task ImportQueryFilter_EnforcesWorkspaceIsolation()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options;
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();

        await using (var platformDb = new AppDbContext(options, new Current(null, true)))
        {
            platformDb.DataImports.AddRange(
                NewImport(workspaceA),
                NewImport(workspaceB));
            await platformDb.SaveChangesAsync();
        }

        await using var workspaceDb = new AppDbContext(options, new Current(workspaceA));
        var visible = await workspaceDb.DataImports.ToListAsync();
        Assert.Single(visible);
        Assert.Equal(workspaceA, visible[0].WorkspaceId);
    }

    private static DataImport NewImport(Guid workspaceId) => new()
    {
        DatasetId = Guid.NewGuid(), WorkspaceId = workspaceId, FileId = Guid.NewGuid(), InitiatedByUserId = Guid.NewGuid(),
        ImportMode = ImportModes.Full, DuplicateBehavior = DuplicateBehaviors.Reject
    };
}
