using System.Text.RegularExpressions;

namespace EnterpriseDataIntelligencePlatform.Data.Analytics;

public static class AnalyticsSchema
{
    // This dated migration resource is immutable once deployed. Future changes need a new migration.
    public static IReadOnlyList<string> UpgradeBatches()
    {
        using var stream = typeof(AnalyticsSchema).Assembly.GetManifestResourceStream(
            "EnterpriseDataIntelligencePlatform.Data.Analytics.Sql.20260831_AnalyticsViews.sql")
            ?? throw new InvalidOperationException("The analytics migration SQL resource is missing.");
        using var reader = new StreamReader(stream);
        return Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    }
}
