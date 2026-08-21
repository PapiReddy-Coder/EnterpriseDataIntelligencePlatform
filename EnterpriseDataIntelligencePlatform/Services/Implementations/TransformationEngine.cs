using System.Globalization;
using System.Text.RegularExpressions;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed record FieldProcessingResult(string? Value, IReadOnlyList<string> Errors, string? TransformationError)
{
    public bool IsValid => TransformationError is null && Errors.Count == 0;
}

public interface ITransformationEngine
{
    FieldProcessingResult Process(string? source, string targetType, FieldMappingModel mapping,
        IReadOnlyDictionary<string, string?> row);
}

public sealed class TransformationEngine : ITransformationEngine
{
    public FieldProcessingResult Process(string? source, string targetType, FieldMappingModel mapping,
        IReadOnlyDictionary<string, string?> row)
    {
        var value = string.IsNullOrEmpty(source) ? mapping.DefaultValue : source;
        try
        {
            foreach (var rule in mapping.Transformations.OrderBy(x => x.Sequence))
                value = Apply(value, rule, row);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            return new(value, [], ex.Message);
        }

        var errors = new List<string>();
        foreach (var rule in mapping.Validations.OrderBy(x => x.Sequence))
            Validate(value, targetType, mapping.IsRequired, rule, errors);
        return new(value, errors, null);
    }

    private static string? Apply(string? value, TransformationRuleModel rule, IReadOnlyDictionary<string, string?> row)
    {
        var p = rule.Parameters ?? new Dictionary<string, string>();
        return rule.Type switch
        {
            TransformationTypes.Trim => value?.Trim(),
            TransformationTypes.Uppercase => value?.ToUpperInvariant(),
            TransformationTypes.Lowercase => value?.ToLowerInvariant(),
            TransformationTypes.Default => string.IsNullOrEmpty(value) && p.TryGetValue("value", out var d) ? d : value,
            TransformationTypes.Replace => value?.Replace(Get(p, "old"), Get(p, "new"), StringComparison.Ordinal),
            TransformationTypes.DateFormat => DateTime.Parse(value ?? "", CultureInfo.InvariantCulture).ToString(Get(p, "format"), CultureInfo.InvariantCulture),
            TransformationTypes.Numeric => decimal.Parse(value ?? "", NumberStyles.Any, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            TransformationTypes.StringLength => value is not null && p.TryGetValue("max", out var max) && value.Length > int.Parse(max) ? value[..int.Parse(max)] : value,
            TransformationTypes.Derived => Derive(p, row),
            _ => throw new InvalidOperationException($"Unsupported transformation type '{rule.Type}'.")
        };
    }

    private static string Derive(IReadOnlyDictionary<string, string> p, IReadOnlyDictionary<string, string?> row)
    {
        var template = Get(p, "template");
        return Regex.Replace(template, "\\{([^}]+)\\}", m => row.TryGetValue(m.Groups[1].Value, out var v) ? v ?? "" : "");
    }

    private static void Validate(string? value, string targetType, bool mappingRequired, ValidationRuleModel rule, List<string> errors)
    {
        var p = rule.Parameters ?? new Dictionary<string, string>();
        bool failed = rule.Type switch
        {
            ValidationRuleTypes.Required => string.IsNullOrWhiteSpace(value),
            ValidationRuleTypes.DataType => !Compatible(value, targetType),
            ValidationRuleTypes.MaximumLength => value?.Length > int.Parse(Get(p, "value")),
            ValidationRuleTypes.MinimumLength => value is not null && value.Length < int.Parse(Get(p, "value")),
            ValidationRuleTypes.NumericRange => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) &&
                (n < decimal.Parse(Get(p, "min"), CultureInfo.InvariantCulture) || n > decimal.Parse(Get(p, "max"), CultureInfo.InvariantCulture)),
            ValidationRuleTypes.DateRange => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                (date < DateTime.Parse(Get(p, "min"), CultureInfo.InvariantCulture) || date > DateTime.Parse(Get(p, "max"), CultureInfo.InvariantCulture)),
            ValidationRuleTypes.AllowedValues => !Get(p, "values").Split('|').Contains(value, StringComparer.OrdinalIgnoreCase),
            ValidationRuleTypes.Pattern => value is not null && !Regex.IsMatch(value, Get(p, "pattern"), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            ValidationRuleTypes.Duplicate => false, // existing KeyHash pipeline owns duplicate evaluation
            _ => false
        };
        if ((mappingRequired && string.IsNullOrWhiteSpace(value)) || failed)
            errors.Add(rule.Message ?? $"{rule.Type} validation failed.");
    }

    private static bool Compatible(string? value, string type) => string.IsNullOrWhiteSpace(value) || type.ToLowerInvariant() switch
    {
        "integer" or "int" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "decimal" or "number" => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _),
        "date" or "datetime" => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "boolean" or "bool" => bool.TryParse(value, out _),
        _ => true
    };

    private static string Get(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var value) ? value : throw new InvalidOperationException($"Parameter '{key}' is required.");
}
