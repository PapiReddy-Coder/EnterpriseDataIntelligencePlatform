using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class BackgroundJobService(
    IServiceScopeFactory scopeFactory,
    IBackgroundJobQueue queue,
    ILogger<BackgroundJobService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await queue.DequeueAsync(stoppingToken);
                using var scope = scopeFactory.CreateScope();
                switch (job.Type)
                {
                    case BackgroundJobTypes.Import:
                        await scope.ServiceProvider.GetRequiredService<IImportProcessor>()
                            .ProcessAsync(job.Id, stoppingToken);
                        break;
                    case BackgroundJobTypes.QualityProfile:
                        await scope.ServiceProvider.GetRequiredService<IDataQualityProfileProcessor>()
                            .ProcessAsync(job.Id, stoppingToken);
                        break;
                    case BackgroundJobTypes.Export:
                        await scope.ServiceProvider.GetRequiredService<IDataSharingService>()
                            .ProcessExportAsync(job.Id, stoppingToken);
                        break;
                    default:
                        logger.LogWarning("Unknown background job type {JobType} for {JobId}.", job.Type, job.Id);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in background job service.");
            }
        }
    }
}
