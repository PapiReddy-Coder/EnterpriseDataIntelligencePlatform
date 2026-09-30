namespace EnterpriseDataIntelligencePlatform.Domain;

public static class DataExportFormats
{
    public const string Csv = "CSV";
    public const string Excel = "Excel";
    public static readonly string[] All = [Csv, Excel];
}

public static class DataExportStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public static readonly string[] All = [Pending, Processing, Completed, Failed, Cancelled];
}

public static class DatasetShareStatuses
{
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";
    public static readonly string[] All = [Active, Expired, Revoked];
}

public static class DatasetShareActions
{
    public const string Shared = "Shared";
    public const string Updated = "Updated";
    public const string Revoked = "Revoked";
}
