using System.Globalization;
using System.Text.RegularExpressions;
using EnterpriseDataIntelligencePlatform.Contracts;
using EnterpriseDataIntelligencePlatform.Domain;

namespace EnterpriseDataIntelligencePlatform.Services.Implementations;

public sealed record QualityRuleFailure(string IssueType, string Rule, string Message, bool AffectsConsistency);

public static class QualityRuleEvaluator
{
    public static IReadOnlyList<QualityRuleFailure> Evaluate(
        string? value,
        DatasetColumn column,
        FieldMappingModel? mapping)
    {
        var failures = new List<QualityRuleFailure>();
        var validations = mapping?.Validations ?? [];
        var required = IsRequired(column, mapping);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                failures.Add(new(QualityIssueTypes.MissingValue, ValidationRuleTypes.Required,
                    $"{column.Name} is required.", false));
            return failures;
        }

        if (!IsCompatible(value, column.DataType))
            failures.Add(new(QualityIssueTypes.InvalidDataType, ValidationRuleTypes.DataType,
                $"Value is not compatible with {column.DataType}.", true));

        var dateFormat = mapping?.Transformations
            .Where(x => x.Type.Equals(TransformationTypes.DateFormat, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Parameters is not null && x.Parameters.TryGetValue("format", out var format) ? format : null)
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (column.DataType == DatasetColumnTypes.DateTime && dateFormat is not null &&
            !DateTime.TryParseExact(value, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            failures.Add(new(QualityIssueTypes.InvalidFormat, TransformationTypes.DateFormat,
                $"Value does not follow the expected date format '{dateFormat}'.", true));

        foreach (var rule in validations.OrderBy(x => x.Sequence))
        {
            if (rule.Type.Equals(ValidationRuleTypes.Required, StringComparison.OrdinalIgnoreCase) ||
                rule.Type.Equals(ValidationRuleTypes.DataType, StringComparison.OrdinalIgnoreCase) ||
                rule.Type.Equals(ValidationRuleTypes.Duplicate, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var failure = EvaluateRule(value, rule);
                if (failure is not null) failures.Add(failure);
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or RegexMatchTimeoutException)
            {
                failures.Add(new(QualityIssueTypes.ValidationFailure, rule.Type,
                    $"Validation rule could not be evaluated: {ex.Message}", false));
            }
        }

        return failures;
    }

    private static QualityRuleFailure? EvaluateRule(string value, ValidationRuleModel rule)
    {
        var parameters = rule.Parameters ?? new Dictionary<string, string>();
        var message = rule.Message ?? $"{rule.Type} validation failed.";
        return rule.Type switch
        {
            ValidationRuleTypes.MaximumLength when value.Length > int.Parse(Get(parameters, "value"), CultureInfo.InvariantCulture) =>
                new(QualityIssueTypes.ValidationFailure, rule.Type, message, false),
            ValidationRuleTypes.MinimumLength when value.Length < int.Parse(Get(parameters, "value"), CultureInfo.InvariantCulture) =>
                new(QualityIssueTypes.ValidationFailure, rule.Type, message, false),
            ValidationRuleTypes.NumericRange when decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) &&
                                                   (number < decimal.Parse(Get(parameters, "min"), CultureInfo.InvariantCulture) ||
                                                    number > decimal.Parse(Get(parameters, "max"), CultureInfo.InvariantCulture)) =>
                new(QualityIssueTypes.ValidationFailure, rule.Type, message, false),
            ValidationRuleTypes.DateRange when DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                                                (date < DateTime.Parse(Get(parameters, "min"), CultureInfo.InvariantCulture) ||
                                                 date > DateTime.Parse(Get(parameters, "max"), CultureInfo.InvariantCulture)) =>
                new(QualityIssueTypes.ValidationFailure, rule.Type, message, false),
            ValidationRuleTypes.AllowedValues when !Get(parameters, "values").Split('|')
                .Contains(value, StringComparer.OrdinalIgnoreCase) =>
                new(QualityIssueTypes.UnexpectedValue, rule.Type, message, true),
            ValidationRuleTypes.Pattern when !Regex.IsMatch(value, Get(parameters, "pattern"),
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) =>
                new(QualityIssueTypes.InvalidFormat, rule.Type, message, true),
            _ => null
        };
    }

    public static bool IsCompatible(string? value, string type) => string.IsNullOrWhiteSpace(value) || type switch
    {
        DatasetColumnTypes.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        DatasetColumnTypes.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
        DatasetColumnTypes.DateTime => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _),
        DatasetColumnTypes.Boolean => value is "0" or "1" || bool.TryParse(value, out _),
        _ => true
    };

    public static bool IsRequired(DatasetColumn column, FieldMappingModel? mapping) =>
        column.IsRequired || mapping?.IsRequired == true ||
        (mapping?.Validations ?? []).Any(x =>
            x.Type.Equals(ValidationRuleTypes.Required, StringComparison.OrdinalIgnoreCase));

    private static string Get(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Parameter '{key}' is required.");
}
