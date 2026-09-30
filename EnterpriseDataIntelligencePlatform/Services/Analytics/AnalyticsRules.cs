using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Data.Analytics;
using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Analytics;

public sealed class AnalyticsRequestException(int statusCode, string code, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class AnalyticsOptions
{
    public int MaxExportRows { get; set; } = 10000;
    public int MaxTrendDays { get; set; } = 366;
    // Caching is opt-in; queries remain live by default.
    public int SummaryCacheSeconds { get; set; } = 0;
}

public static class AnalyticsRules
{
    public static decimal? Percentage(long numerator, long denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);
    public static decimal? Round(decimal? value) =>
        value.HasValue ? Math.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;

    public static ImportSummary Finish(ImportSummary summary)
    {
        summary.ImportSuccessRate = Percentage(summary.SuccessfulImports,
            summary.SuccessfulImports + summary.FailedImports + summary.CompletedWithErrors);
        summary.RecordSuccessRate = Percentage(summary.RecordsSuccessfullyImported, summary.TotalRecordsProcessed);
        if (summary.AverageProcessingTimeMilliseconds.HasValue)
            summary.AverageProcessingTimeMilliseconds = Math.Round(summary.AverageProcessingTimeMilliseconds.Value, 2);
        return summary;
    }

    public static QualitySummary Finish(QualitySummary summary)
    {
        summary.OverallQualityScore = Round(summary.OverallQualityScore);
        summary.Completeness = Round(summary.Completeness);
        summary.Validity = Round(summary.Validity);
        summary.Uniqueness = Round(summary.Uniqueness);
        summary.Consistency = Round(summary.Consistency);
        return summary;
    }

    public static IReadOnlyList<string> AttentionReasons(DatasetAnalyticsRow row)
    {
        List<string> reasons = [];
        if (row.NoCompletedProfile) reasons.Add("NoCompletedProfile");
        if (row.NoProfileAfterLatestImport) reasons.Add("NoProfileAfterLatestImport");
        if (row.FailedQualityThreshold) reasons.Add("QualityThresholdFailed");
        if (row.LatestImportStatus == ImportStatuses.Failed) reasons.Add("LatestImportFailed");
        if (row.LatestImportStatus == ImportStatuses.CompletedWithErrors) reasons.Add("LatestImportCompletedWithErrors");
        if (row.SignificantQualityIssues) reasons.Add("SignificantQualityIssues");
        return reasons;
    }

    public static void Validate(AnalyticsQuery query)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            Invalid("Page must be positive, PageSize must be 1–100, and the offset must fit Int32.");
        if (query.Search?.Length > 200) Invalid("Search cannot exceed 200 characters.");
        if (new[] { query.WorkspaceId, query.DatasetId, query.CategoryId, query.OwnerId }.Any(x => x == Guid.Empty))
            Invalid("Empty GUID filters are not allowed.");
        if (query.FromUtc?.Kind == DateTimeKind.Local || query.ToUtc?.Kind == DateTimeKind.Local)
            Invalid("Use UTC timestamps ending in Z.");
        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc >= query.ToUtc)
            Invalid("FromUtc must be earlier than ToUtc (the upper bound is exclusive).");
        if (query.Status is not null && !DatasetStatuses.All.Contains(query.Status))
            Invalid("Status must be Draft, Active, or Archived.");
        if (query.ImportStatus is not null && !new[] { ImportStatuses.Created, ImportStatuses.Queued,
                ImportStatuses.Processing, ImportStatuses.Completed, ImportStatuses.CompletedWithErrors,
                ImportStatuses.Failed, ImportStatuses.Cancelled }.Contains(query.ImportStatus))
            Invalid("ImportStatus is not recognized.");
        if (query.QualityStatus is not null && !new[] { "Passed", "Failed", "N/A", "Unprofiled" }.Contains(query.QualityStatus))
            Invalid("QualityStatus must be Passed, Failed, N/A, or Unprofiled.");
        if (!new[] { "asc", "desc" }.Contains(query.SortDirection))
            Invalid("SortDirection must be asc or desc.");
    }

    public static void ValidateDateField(AnalyticsQuery query, params string[] allowed)
    {
        if (query.DateField is not null && !allowed.Contains(query.DateField)) Invalid($"DateField must be one of: {string.Join(", ", allowed)}.");
    }

    public static void ValidateSort(AnalyticsQuery query, params string[] allowed)
    {
        if (query.SortBy is not null && !allowed.Contains(query.SortBy))
            Invalid($"SortBy must be one of: {string.Join(", ", allowed)}.");
    }

    public static (DateTime From, DateTime To) TrendRange(TrendQuery query, int maxDays, DateTime now)
    {
        if (!new[] { "daily", "weekly", "monthly" }.Contains(query.Grouping))
            Invalid("Grouping must be daily, weekly, or monthly.");
        var to = query.ToUtc ?? DateTime.SpecifyKind(now.Date.AddDays(1), DateTimeKind.Utc);
        var from = query.FromUtc ?? to.AddDays(-30);
        if (from >= to || (to - from).TotalDays > maxDays)
            Invalid($"Trend range must be positive and no longer than {maxDays} days.");
        return (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc));
    }

    public static DateTime Bucket(DateTime value, string grouping) => grouping switch
    {
        "daily" => value.Date,
        "weekly" => value.Date.AddDays(-(((int)value.DayOfWeek + 6) % 7)),
        "monthly" => new DateTime(value.Year, value.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        _ => throw new AnalyticsRequestException(400, "InvalidParameters", "Invalid grouping.")
    };

    public static void Invalid(string message) =>
        throw new AnalyticsRequestException(400, "InvalidParameters", message);
}
