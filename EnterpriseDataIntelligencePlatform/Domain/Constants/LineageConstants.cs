namespace EnterpriseDataIntelligencePlatform.Domain;

public static class LineageEntityTypes
{
    public const string DataSource = "DataSource";
    public const string Dataset = "Dataset";
    public const string DatasetVersion = "DatasetVersion";
    public const string SourceFile = "SourceFile";
    public const string Import = "Import";
    public const string Transformation = "Transformation";
    public const string QualityProfile = "QualityProfile";

    public static readonly string[] All =
    [
        DataSource, Dataset, DatasetVersion, SourceFile, Import, Transformation, QualityProfile
    ];

    public static readonly string[] Manual = [Dataset, DatasetVersion];
    public static readonly string[] Sources = [DataSource, SourceFile];
}

public static class LineageRelationshipTypes
{
    public const string VersionOf = "VersionOf";
    public const string SourcedFrom = "SourcedFrom";
    public const string ProvidesData = "ProvidesData";
    public const string Loads = "Loads";
    public const string Configures = "Configures";
    public const string AppliesTransformation = "AppliesTransformation";
    public const string Profiles = "Profiles";
    public const string Feeds = "Feeds";

    public static readonly string[] All =
    [
        VersionOf, SourcedFrom, ProvidesData, Loads, Configures,
        AppliesTransformation, Profiles, Feeds
    ];

    public static readonly string[] Manual = [Feeds];
}

public static class LineageDirections
{
    public const string Complete = "complete";
    public const string Upstream = "upstream";
    public const string Downstream = "downstream";
    public static readonly string[] All = [Complete, Upstream, Downstream];
}
