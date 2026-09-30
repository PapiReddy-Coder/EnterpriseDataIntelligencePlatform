using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record SaveTransformationConfigurationRequest(
    [param: Required] IReadOnlyList<FieldMappingModel> Mappings);

public sealed record FieldMappingModel(
    [param: Required] string SourceColumn,
    Guid TargetColumnId,
    bool IsRequired,
    string? DefaultValue,
    IReadOnlyList<TransformationRuleModel> Transformations,
    IReadOnlyList<ValidationRuleModel> Validations);

public sealed record TransformationRuleModel(
    [param: Required] string Type,
    int Sequence,
    IReadOnlyDictionary<string, string>? Parameters);

public sealed record ValidationRuleModel(
    [param: Required] string Type,
    int Sequence,
    string? Message,
    IReadOnlyDictionary<string, string>? Parameters);

public sealed record TransformationConfigurationResponse(
    Guid Id, Guid DatasetId, int Version, bool IsActive,
    IReadOnlyList<FieldMappingModel> Mappings, Guid CreatedByUserId, DateTime CreatedAtUtc);

public sealed record TransformationPreviewRequest([param: Range(1, 100)] int Limit = 25);
public sealed record TransformationPreviewValue(string SourceColumn, string TargetField, string? OriginalValue,
    string? TransformedValue, bool IsValid, IReadOnlyList<string> Errors);
public sealed record TransformationPreviewRow(int RowNumber, bool IsValid, IReadOnlyList<TransformationPreviewValue> Values);
public sealed record TransformationPreviewResponse(Guid ImportId, Guid ConfigurationId, int ConfigurationVersion,
    IReadOnlyList<TransformationPreviewRow> Rows);
