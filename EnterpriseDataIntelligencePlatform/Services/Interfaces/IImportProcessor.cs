namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IImportProcessor
{
    Task ProcessAsync(Guid importId, CancellationToken hostToken);
}
