using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Data.Analytics;

public static class AnalyticsModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        // Preserve the pre-existing convention index while adding covering indexes.
        builder.Entity<DataImport>().HasIndex(x => x.DatasetId);
        builder.Entity<DataImport>().HasIndex(x => new { x.DatasetId, x.CreatedAtUtc, x.Id },
                "IX_Analytics_Imports_Dataset_Created").IsDescending(false, true, true)
            .IncludeProperties(x => new { x.WorkspaceId, x.Status, x.CompletedAtUtc });
        builder.Entity<DataImport>().HasIndex(x => new { x.WorkspaceId, x.CompletedAtUtc },
                "IX_Analytics_Imports_Workspace_Completed")
            .IncludeProperties(x => new { x.DatasetId, x.Status, x.StartedAtUtc, x.TotalRecords, x.SuccessfullyImportedRecords, x.RejectedRecords });
        builder.Entity<DataQualityProfileRun>().HasIndex(x => new { x.DatasetId, x.CompletedAtUtc, x.CreatedAtUtc, x.Id },
                "IX_Analytics_Profiles_Dataset_Completed").IsDescending(false, true, true, true)
            .HasFilter("[Status] = N'Completed'").IncludeProperties(x => new { x.WorkspaceId, x.OverallQualityScore, x.ThresholdStatus });
        builder.Entity<DataQualityProfileRun>().HasIndex(x => new { x.WorkspaceId, x.CompletedAtUtc },
                "IX_Analytics_Profiles_Workspace_Completed")
            .IncludeProperties(x => new { x.DatasetId, x.Status, x.OverallQualityScore });
        builder.Entity<DataQualityIssue>().HasIndex(x => new { x.ProfileRunId, x.Severity },
                "IX_Analytics_QualityIssues_Profile_Severity")
            .IncludeProperties(x => new { x.DatasetId, x.WorkspaceId, x.IssueType });
        builder.Entity<DatasetAnalyticsRow>().HasKey(x => x.DatasetId);
        builder.Entity<DatasetAnalyticsRow>().ToView("vw_DatasetAnalytics", "dbo");
        builder.Entity<ImportAnalyticsRow>().HasKey(x => x.ImportId);
        builder.Entity<ImportAnalyticsRow>().ToView("vw_ImportAnalytics", "dbo");
        builder.Entity<QualityTrendRow>().HasKey(x => x.ProfileRunId);
        builder.Entity<QualityTrendRow>().ToView("vw_QualityTrendSource", "dbo");
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        foreach (var type in new[] { typeof(DatasetAnalyticsRow), typeof(ImportAnalyticsRow), typeof(QualityTrendRow) })
        foreach (var property in builder.Entity(type).Metadata.GetProperties())
        {
            if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
            {
                property.SetPrecision(5);
                property.SetScale(2);
            }
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetValueConverter(utc);
        }
    }
}
