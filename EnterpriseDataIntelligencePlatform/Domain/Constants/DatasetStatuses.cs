namespace EnterpriseDataIntelligencePlatform.Domain;

public static class DatasetStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Archived = "Archived";

    // Retained only for backward source compatibility.
    // Soft delete is represented by Dataset.IsDeleted in the current module.
    public const string SoftDeleted = "Soft Deleted";

    public static readonly string[] All = [Draft, Active, Archived];
}
