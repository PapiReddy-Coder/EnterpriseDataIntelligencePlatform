namespace EnterpriseDataIntelligencePlatform.Data.Seed;

/// <summary>
/// Creates deterministic seed identifiers while preserving the identifiers
/// already used by the existing database and EF Core migrations.
/// </summary>
public static class SeedIdFactory
{
    private const string PermissionPrefix = "10000000-0000-0000-0000-";

    public static Guid Permission(int sequence)
    {
        // sequence is an Int32; every positive value fits in the 12-digit GUID suffix.
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return Guid.Parse($"{PermissionPrefix}{sequence:D12}");
    }
}
