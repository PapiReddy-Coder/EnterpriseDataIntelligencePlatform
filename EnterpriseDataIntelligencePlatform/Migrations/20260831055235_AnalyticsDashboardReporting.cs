using EnterpriseDataIntelligencePlatform.Data.Analytics;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EnterpriseDataIntelligencePlatform.Migrations;

public partial class AnalyticsDashboardReporting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var batch in AnalyticsSchema.UpgradeBatches())
            // CREATE VIEW must begin its own batch, including in an EF idempotent script.
            migrationBuilder.Sql(batch.TrimStart().StartsWith("CREATE OR ALTER VIEW", StringComparison.OrdinalIgnoreCase)
                ? "EXEC(N'" + batch.Replace("'", "''") + "');"
                : batch);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP VIEW IF EXISTS dbo.vw_QualityTrendSource;
            DROP VIEW IF EXISTS dbo.vw_ImportAnalytics;
            DROP VIEW IF EXISTS dbo.vw_DatasetAnalytics;
            DROP INDEX IF EXISTS IX_Analytics_Imports_Dataset_Created ON dbo.DataImports;
            DROP INDEX IF EXISTS IX_Analytics_Imports_Workspace_Completed ON dbo.DataImports;
            DROP INDEX IF EXISTS IX_Analytics_Profiles_Dataset_Completed ON dbo.DataQualityProfileRuns;
            DROP INDEX IF EXISTS IX_Analytics_Profiles_Workspace_Completed ON dbo.DataQualityProfileRuns;
            DROP INDEX IF EXISTS IX_Analytics_QualityIssues_Profile_Severity ON dbo.DataQualityIssues;
            DELETE rp FROM dbo.RolePermissions rp
              JOIN dbo.Permissions p ON p.Id=rp.PermissionId WHERE p.Name=N'reports.export';
            DELETE FROM dbo.Permissions WHERE Name=N'reports.export';
            """);
    }
}
