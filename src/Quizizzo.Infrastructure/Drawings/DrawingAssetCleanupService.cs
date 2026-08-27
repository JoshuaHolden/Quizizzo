using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Drawings;

public sealed class DrawingAssetCleanupService(
    IDrawingAssetStore assetStore,
    IOptions<DrawingAssetStoreOptions> options,
    TimeProvider timeProvider,
    ILogger<DrawingAssetCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoffUtc = timeProvider.GetUtcNow().Subtract(options.Value.RetentionPeriod);
                var deleted = await assetStore.DeleteExpiredAsync(cutoffUtc, stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {DrawingAssetCount} expired drawing assets", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Drawing asset expiry sweep failed");
            }

            await Task.Delay(options.Value.CleanupInterval, timeProvider, stoppingToken);
        }
    }
}
