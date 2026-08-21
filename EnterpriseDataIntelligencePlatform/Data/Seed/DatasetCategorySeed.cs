using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Data.Seed;

public static class DatasetCategorySeed
{
    public static readonly DatasetCategory[] Data =
    [
        Create("20000000-0000-0000-0000-000000000001", "Human Resources", "HR and workforce datasets"),
        Create("20000000-0000-0000-0000-000000000002", "Finance", "Financial and accounting datasets"),
        Create("20000000-0000-0000-0000-000000000003", "Sales", "Sales and revenue datasets"),
        Create("20000000-0000-0000-0000-000000000004", "Marketing", "Marketing and campaign datasets"),
        Create("20000000-0000-0000-0000-000000000005", "Operations", "Operational datasets"),
        Create("20000000-0000-0000-0000-000000000006", "Customer", "Customer and relationship datasets"),
        Create("20000000-0000-0000-0000-000000000007", "Reference", "Shared master and reference datasets"),
        Create("20000000-0000-0000-0000-000000000008", "Other", "Unclassified datasets")
    ];

    private static DatasetCategory Create(string id, string name, string description) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Description = description,
        IsActive = true,
        CreatedAtUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)
    };
}
