
namespace EnterpriseDataIntelligencePlatform.Contracts;

public class CreateImportRequest
{
    public Guid FileId { get; set; }

    public string ImportMode { get; set; } = string.Empty;

    public string DuplicateBehavior { get; set; } = string.Empty;

    public string CsvDelimiter { get; set; } = ",";

    public bool FirstRowContainsHeaders { get; set; } = true;

    public List<string> KeyColumns { get; set; } = new();

    public string? WorksheetName { get; set; }
}
