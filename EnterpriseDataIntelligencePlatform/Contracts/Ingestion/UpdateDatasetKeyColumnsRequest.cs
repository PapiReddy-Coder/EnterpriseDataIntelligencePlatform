using System.ComponentModel.DataAnnotations;

namespace EnterpriseDataIntelligencePlatform.Contracts;

public sealed record UpdateDatasetKeyColumnsRequest(
    [Required] IReadOnlyList<string> KeyColumns);