namespace EnterpriseDataIntelligencePlatform.Domain;

public static class QualityProfileStatuses
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] Active = [Queued, Processing];
    public static readonly string[] Terminal = [Completed, Failed, Cancelled];
}

public static class QualityProfileTriggers
{
    public const string Automatic = "Automatic";
    public const string Manual = "Manual";
}

public static class QualityThresholdStatuses
{
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public const string NotApplicable = "N/A";
}

public static class QualityIssueTypes
{
    public const string MissingValue = "Missing Value";
    public const string InvalidDataType = "Invalid Data Type";
    public const string DuplicateValue = "Duplicate Value";
    public const string InvalidFormat = "Invalid Format";
    public const string ValidationFailure = "Validation Failure";
    public const string UnexpectedValue = "Unexpected Value";
}

public static class QualityDefaults
{
    public const decimal CompletenessThreshold = 90m;
    public const decimal ValidityThreshold = 95m;
    public const decimal UniquenessThreshold = 90m;
    public const decimal ConsistencyThreshold = 90m;
    public const decimal OverallThreshold = 90m;

    public const decimal CompletenessWeight = 0.30m;
    public const decimal ValidityWeight = 0.30m;
    public const decimal UniquenessWeight = 0.20m;
    public const decimal ConsistencyWeight = 0.20m;
    public const string ScoringVersion = "1.0";
}
