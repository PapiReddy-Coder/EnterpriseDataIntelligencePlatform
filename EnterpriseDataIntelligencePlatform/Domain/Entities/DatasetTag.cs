namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetTag
{
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
