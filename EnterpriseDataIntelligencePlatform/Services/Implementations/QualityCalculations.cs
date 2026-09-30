using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public static class QualityCalculations
{
    public static decimal? Percentage(int numerator, int denominator) =>
        denominator <= 0 ? null : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);

    public static decimal? WeightedScore(params (decimal? Score, decimal Weight)[] dimensions)
    {
        var applicable = dimensions.Where(x => x.Score.HasValue && x.Weight > 0).ToArray();
        if (applicable.Length == 0) return null;
        var totalWeight = applicable.Sum(x => x.Weight);
        return Math.Round(applicable.Sum(x => x.Score!.Value * x.Weight) / totalWeight, 2,
            MidpointRounding.AwayFromZero);
    }

    public static decimal? OverallScore(decimal? completeness, decimal? validity, decimal? uniqueness, decimal? consistency) =>
        WeightedScore(
            (completeness, QualityDefaults.CompletenessWeight),
            (validity, QualityDefaults.ValidityWeight),
            (uniqueness, QualityDefaults.UniquenessWeight),
            (consistency, QualityDefaults.ConsistencyWeight));

    public static int DuplicateCount(int applicableValues, int distinctValues) =>
        Math.Max(0, applicableValues - distinctValues);

    public static int CaseInsensitiveDistinctCount(IEnumerable<string?> values) =>
        values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public static bool CanStart(string status) => status == QualityProfileStatuses.Queued;
    public static bool CanCancel(string status) => status is QualityProfileStatuses.Queued or QualityProfileStatuses.Processing;

    public static string EvaluateThresholds(
        int totalRecords,
        decimal? completeness,
        decimal? validity,
        decimal? uniqueness,
        decimal? consistency,
        decimal? overall,
        decimal minimumCompleteness,
        decimal minimumValidity,
        decimal minimumUniqueness,
        decimal minimumConsistency,
        decimal minimumOverall)
    {
        if (totalRecords == 0 || overall is null) return QualityThresholdStatuses.NotApplicable;
        var passed = (!completeness.HasValue || completeness.Value >= minimumCompleteness)
                     && (!validity.HasValue || validity.Value >= minimumValidity)
                     && (!uniqueness.HasValue || uniqueness.Value >= minimumUniqueness)
                     && (!consistency.HasValue || consistency.Value >= minimumConsistency)
                     && overall.Value >= minimumOverall;
        return passed ? QualityThresholdStatuses.Passed : QualityThresholdStatuses.Failed;
    }
}
