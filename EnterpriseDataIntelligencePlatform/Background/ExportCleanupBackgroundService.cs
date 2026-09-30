using EnterpriseDataIntelligencePlatform.Services.Interfaces;

namespace EnterpriseDataIntelligencePlatform.Background;

public sealed class ExportCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IDataSharingService>()
                    .CleanupExpiredExportsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Export cleanup failed."); }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
