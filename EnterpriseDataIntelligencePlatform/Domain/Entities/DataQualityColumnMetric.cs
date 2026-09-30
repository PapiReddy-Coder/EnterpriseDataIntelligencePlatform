namespace EnterpriseDataIntelligencePlatform.Domain;

public sealed class DataQualityColumnMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileRunId { get; set; }
    public Guid DatasetColumnId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = DatasetColumnTypes.String;
    public bool IsRequired { get; set; }
    public bool IsKey { get; set; }
    public int TotalRecords { get; set; }
    public int NonNullRecords { get; set; }
    public int NullCount { get; set; }
    public decimal? NullPercentage { get; set; }
    public int DistinctCount { get; set; }
    public int UniqueCount { get; set; }
    public int DuplicateCount { get; set; }
    public int InvalidCount { get; set; }
    public decimal? CompletenessScore { get; set; }
    public decimal? ValidityScore { get; set; }
    public decimal? UniquenessScore { get; set; }
    public decimal? ConsistencyScore { get; set; }
    public decimal? QualityScore { get; set; }
    public string? MinimumValue { get; set; }
    public string? MaximumValue { get; set; }
    public decimal? AverageValue { get; set; }
    public DataQualityProfileRun ProfileRun { get; set; } = null!;
    public DatasetColumn DatasetColumn { get; set; } = null!;
}
