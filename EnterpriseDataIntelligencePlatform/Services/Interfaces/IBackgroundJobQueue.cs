namespace EnterpriseDataIntelligencePlatform.Services.Interfaces;

public sealed record BackgroundJob(string Type, Guid Id);

public static class BackgroundJobTypes
{
    public const string Import = "Import";
    public const string QualityProfile = "QualityProfile";
    public const string Export = "Export";
}

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(BackgroundJob job, CancellationToken ct);
    ValueTask<BackgroundJob> DequeueAsync(CancellationToken ct);
}
