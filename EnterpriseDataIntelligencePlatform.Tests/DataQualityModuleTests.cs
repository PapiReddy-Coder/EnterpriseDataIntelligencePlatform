using EnterpriseDataIntelligencePlatform.Authorization;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Controllers;
using EnterpriseDataIntelligencePlatform.Data;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Infrastructure;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class DataQualityModuleTests
{
    private sealed class Current(Guid? workspaceId, bool platform = false) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? WorkspaceId => workspaceId;
        public Guid? SessionId => Guid.NewGuid();
        public bool IsPlatformAdministrator => platform;
    }

    [Fact]
    public void Percentage_CalculatesCompletenessAndReturnsNullForNoData()
    {
        Assert.Equal(90m, QualityCalculations.Percentage(90, 100));
        Assert.Null(QualityCalculations.Percentage(0, 0));
    }

    [Fact]
    public void Uniqueness_IsCaseInsensitiveAndDuplicateCountUsesTotalMinusDistinct()
    {
        var distinct = QualityCalculations.CaseInsensitiveDistinctCount(["ABC", "abc", "Abc", "XYZ", null]);
        Assert.Equal(2, distinct);
        Assert.Equal(2, QualityCalculations.DuplicateCount(4, distinct));
        Assert.Equal(50m, QualityCalculations.Percentage(distinct, 4));
    }

    [Fact]
    public void WeightedScore_RenormalizesWhenUniquenessIsNotApplicable()
    {
        var score = QualityCalculations.WeightedScore((90m, .30m), (100m, .30m), (null, .20m), (80m, .20m));
        Assert.Equal(91.25m, score);
    }

    [Fact]
    public void ValidationStatistics_RespectSchemaRequiredAndConfiguredRules()
    {
        var column = new DatasetColumn { Name = "Status", DataType = DatasetColumnTypes.String, IsRequired = true };
        var mapping = new FieldMappingModel("Status", column.Id, false, null, [],
            [new(ValidationRuleTypes.AllowedValues, 1, "Unexpected status", new Dictionary<string, string> { ["values"] = "ACTIVE|INACTIVE" })]);

        Assert.Contains(QualityRuleEvaluator.Evaluate(null, column, mapping), x => x.IssueType == QualityIssueTypes.MissingValue);
        Assert.Contains(QualityRuleEvaluator.Evaluate("UNKNOWN", column, mapping), x => x.IssueType == QualityIssueTypes.UnexpectedValue);
        Assert.Empty(QualityRuleEvaluator.Evaluate("active", column, mapping));

        var optional = new DatasetColumn { Name = "Comment", DataType = DatasetColumnTypes.String, IsRequired = false };
        var requiredByRule = new FieldMappingModel("Comment", optional.Id, false, null, [],
            [new(ValidationRuleTypes.Required, 1, null, null)]);
        Assert.True(QualityRuleEvaluator.IsRequired(optional, requiredByRule));
        Assert.Contains(QualityRuleEvaluator.Evaluate(null, optional, requiredByRule),
            x => x.IssueType == QualityIssueTypes.MissingValue);
    }

    [Fact]
    public void ThresholdEvaluation_UsesEveryApplicableDimensionAndOverallScore()
    {
        Assert.Equal(QualityThresholdStatuses.Passed,
            QualityCalculations.EvaluateThresholds(10, 90m, 95m, 90m, 90m, 91m, 90m, 95m, 90m, 90m, 90m));
        Assert.Equal(QualityThresholdStatuses.Failed,
            QualityCalculations.EvaluateThresholds(10, 89m, 100m, null, 100m, 95m, 90m, 95m, 90m, 90m, 90m));
        Assert.Equal(QualityThresholdStatuses.NotApplicable,
            QualityCalculations.EvaluateThresholds(0, null, null, null, null, null, 90m, 95m, 90m, 90m, 90m));
    }

    [Fact]
    public void SystemThresholdDefaults_MatchTask19Decision()
    {
        Assert.Equal(90m, QualityDefaults.CompletenessThreshold);
        Assert.Equal(95m, QualityDefaults.ValidityThreshold);
        Assert.Equal(90m, QualityDefaults.UniquenessThreshold);
        Assert.Equal(90m, QualityDefaults.ConsistencyThreshold);
        Assert.Equal(90m, QualityDefaults.OverallThreshold);
    }

    [Fact]
    public void ProfilingStatusTransitions_AreControlled()
    {
        Assert.True(QualityCalculations.CanStart(QualityProfileStatuses.Queued));
        Assert.False(QualityCalculations.CanStart(QualityProfileStatuses.Processing));
        Assert.True(QualityCalculations.CanCancel(QualityProfileStatuses.Queued));
        Assert.True(QualityCalculations.CanCancel(QualityProfileStatuses.Processing));
        Assert.False(QualityCalculations.CanCancel(QualityProfileStatuses.Completed));
    }

    [Fact]
    public void DataQualityController_UsesPermissionBasedAuthorization()
    {
        var methods = typeof(DataQualityController).GetMethods();
        Assert.Contains(methods, x => x.Name == "Start" && HasPermission(x, Permissions.QualityProfilesRun));
        Assert.Contains(methods, x => x.Name == "Latest" && HasPermission(x, Permissions.QualityProfilesView));
        Assert.Contains(methods, x => x.Name == "Issues" && HasPermission(x, Permissions.QualityIssuesView));
        Assert.Contains(methods, x => x.Name == "DatasetIssues" && HasPermission(x, Permissions.QualityIssuesView));
        Assert.Contains(methods, x => x.Name == "UpsertThresholds" && HasPermission(x, Permissions.QualityThresholdsManage));
    }

    [Fact]
    public async Task QualityProfileQueryFilter_EnforcesWorkspaceIsolation()
    {
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using (var seed = new AppDbContext(options, new Current(null, true)))
        {
            seed.DataQualityProfileRuns.AddRange(
                new DataQualityProfileRun { DatasetId = Guid.NewGuid(), WorkspaceId = workspaceA },
                new DataQualityProfileRun { DatasetId = Guid.NewGuid(), WorkspaceId = workspaceB });
            await seed.SaveChangesAsync();
        }

        await using var scoped = new AppDbContext(options, new Current(workspaceA));
        var visible = await scoped.DataQualityProfileRuns.AsNoTracking().ToListAsync();
        Assert.Single(visible);
        Assert.Equal(workspaceA, visible[0].WorkspaceId);
    }

    private static bool HasPermission(System.Reflection.MethodInfo method, string permission) =>
        method.GetCustomAttributes(typeof(HasPermissionAttribute), true)
            .Cast<HasPermissionAttribute>().Any(x => x.Policy == $"Permission:{permission}");
}
