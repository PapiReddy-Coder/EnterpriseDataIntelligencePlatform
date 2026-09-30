using EnterpriseDataIntelligencePlatform.Domain;
using Xunit;

namespace EnterpriseDataIntelligencePlatform.Tests;

public sealed class DataSharingModuleTests
{
    [Fact]
    public void ExportFormats_ContainCsvAndExcel()
    {
        Assert.Contains(DataExportFormats.Csv, DataExportFormats.All);
        Assert.Contains(DataExportFormats.Excel, DataExportFormats.All);
    }

    [Fact]
    public void ExportStatuses_ContainRequiredLifecycleValues()
    {
        Assert.Contains(DataExportStatuses.Pending, DataExportStatuses.All);
        Assert.Contains(DataExportStatuses.Processing, DataExportStatuses.All);
        Assert.Contains(DataExportStatuses.Completed, DataExportStatuses.All);
        Assert.Contains(DataExportStatuses.Failed, DataExportStatuses.All);
        Assert.Contains(DataExportStatuses.Cancelled, DataExportStatuses.All);
    }

    [Fact]
    public void ShareAccessLevels_UseExistingGovernanceLevels()
    {
        Assert.Equal(1, DatasetAccessLevels.Rank(DatasetAccessLevels.Read));
        Assert.Equal(2, DatasetAccessLevels.Rank(DatasetAccessLevels.Write));
        Assert.Equal(3, DatasetAccessLevels.Rank(DatasetAccessLevels.Manage));
    }

    [Fact]
    public void ShareStatuses_ContainRequiredValues()
    {
        Assert.Contains(DatasetShareStatuses.Active, DatasetShareStatuses.All);
        Assert.Contains(DatasetShareStatuses.Expired, DatasetShareStatuses.All);
        Assert.Contains(DatasetShareStatuses.Revoked, DatasetShareStatuses.All);
    }
}
