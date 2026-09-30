namespace EnterpriseDataIntelligencePlatform.Domain;

public static class ImportStatuses
{
    public const string Created = "Created";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "Completed With Errors";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static readonly string[] Active = [Queued, Processing];
    public static readonly string[] Terminal = [Completed, CompletedWithErrors, Failed, Cancelled];
}

public static class ImportModes
{
    public const string Full = "Full";
    public const string Append = "Append";
    public static readonly string[] All = [Full, Append];
}

public static class DuplicateBehaviors
{
    public const string Skip = "Skip";
    public const string Update = "Update";
    public const string Reject = "Reject";
    public static readonly string[] All = [Skip, Update, Reject];
}

public static class InvalidRecordBehaviors
{
    public const string Skip = "Skip";
}

public static class DatasetColumnTypes
{
    public const string String = "String";
    public const string Integer = "Integer";
    public const string Decimal = "Decimal";
    public const string Boolean = "Boolean";
    public const string DateTime = "DateTime";
}
