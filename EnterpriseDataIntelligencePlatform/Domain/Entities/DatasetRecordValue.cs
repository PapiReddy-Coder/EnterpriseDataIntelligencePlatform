namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DatasetRecordValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetRecordId { get; set; }
    public Guid DatasetColumnId { get; set; }
    public string? RawValue { get; set; }
    public string? StringValue { get; set; }
    public long? IntegerValue { get; set; }
    public decimal? DecimalValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTime? DateTimeValue { get; set; }
    public DatasetRecord DatasetRecord { get; set; } = null!;
    public DatasetColumn DatasetColumn { get; set; } = null!;
}
