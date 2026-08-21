namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IImportCancellationRegistry
{
    CancellationToken Register(Guid importId, CancellationToken hostToken);
    bool RequestCancellation(Guid importId);
    void Unregister(Guid importId);
}
