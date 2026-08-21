namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public interface IImportJobQueue
{
    ValueTask EnqueueAsync(Guid importId, CancellationToken ct);
    ValueTask<Guid> DequeueAsync(CancellationToken ct);
}
