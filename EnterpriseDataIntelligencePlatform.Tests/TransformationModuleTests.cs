using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;
using EnterpriseDataIntelligencePlatform.Services.Implementations;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class TransformationModuleTests
{
    private readonly TransformationEngine engine = new();

    [Fact]
    public void Applies_Default_Then_Ordered_Transformations_Then_Validation()
    {
        var mapping = Map(" unknown ",
            [new(TransformationTypes.Trim, 1, null), new(TransformationTypes.Uppercase, 2, null)],
            [new(ValidationRuleTypes.AllowedValues, 1, null, new Dictionary<string, string> { ["values"] = "UNKNOWN|ACTIVE" })]);
        var result = engine.Process(null, "String", mapping, new Dictionary<string, string?>());
        Assert.True(result.IsValid); Assert.Equal("UNKNOWN", result.Value);
    }

    [Fact]
    public void Evaluates_All_Validation_Rules()
    {
        var mapping = Map(null, [],
            [new(ValidationRuleTypes.MinimumLength, 1, "Too short", new Dictionary<string, string> { ["value"] = "5" }),
             new(ValidationRuleTypes.Pattern, 2, "Bad pattern", new Dictionary<string, string> { ["pattern"] = "^[A-Z]+$" })]);
        var result = engine.Process("a1", "String", mapping, new Dictionary<string, string?>());
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Invalid_Numeric_Conversion_Is_Transformation_Error()
    {
        var result = engine.Process("abc", "Decimal", Map(null, [new(TransformationTypes.Numeric, 1, null)], []), new Dictionary<string, string?>());
        Assert.False(result.IsValid); Assert.NotNull(result.TransformationError);
    }

    [Fact]
    public void Derived_Field_Uses_Row_Columns()
    {
        var result = engine.Process(null, "String", Map(null,
            [new(TransformationTypes.Derived, 1, new Dictionary<string, string> { ["template"] = "{First} {Last}" })], []),
            new Dictionary<string, string?> { ["First"] = "Ada", ["Last"] = "Lovelace" });
        Assert.Equal("Ada Lovelace", result.Value);
    }

    private static FieldMappingModel Map(string? defaultValue, IReadOnlyList<TransformationRuleModel> transforms,
        IReadOnlyList<ValidationRuleModel> validations) => new("Source", Guid.NewGuid(), false, defaultValue, transforms, validations);
}
