namespace EnterpriseDataIntelligencePlatform.Domain;

public static class TransformationTypes
{
    public const string Trim = "Trim";
    public const string Uppercase = "Uppercase";
    public const string Lowercase = "Lowercase";
    public const string Replace = "Replace";
    public const string Default = "Default";
    public const string DateFormat = "DateFormat";
    public const string Numeric = "Numeric";
    public const string StringLength = "StringLength";
    public const string Derived = "Derived";
    public static readonly string[] All = [Trim, Uppercase, Lowercase, Replace, Default, DateFormat, Numeric, StringLength, Derived];
}

public static class ValidationRuleTypes
{
    public const string Required = "Required";
    public const string DataType = "DataType";
    public const string MaximumLength = "MaximumLength";
    public const string MinimumLength = "MinimumLength";
    public const string NumericRange = "NumericRange";
    public const string DateRange = "DateRange";
    public const string AllowedValues = "AllowedValues";
    public const string Pattern = "Pattern";
    public const string Duplicate = "Duplicate";
}

public static class ImportErrorTypes
{
    public const string Transformation = "Transformation Error";
    public const string Validation = "Validation Error";
    public const string Duplicate = "Duplicate Error";
    public const string Processing = "Processing Error";
}
