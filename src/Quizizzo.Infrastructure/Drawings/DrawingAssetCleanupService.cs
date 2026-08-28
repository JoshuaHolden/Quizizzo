using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Drawings;

public sealed partial class DrawingAssetCleanupService(
    IDrawingAssetStore assetStore,
    IServiceScopeFactory scopeFactory,
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
                await using var scope = scopeFactory.CreateAsyncScope();
                var metadata = scope.ServiceProvider.GetRequiredService<IDrawingAssetMetadataRepository>();
                var deletedMetadata = await metadata.DeleteExpiredAsync(
                    timeProvider.GetUtcNow(), stoppingToken);
                if (deleted > 0)
                {
                    LogDeletedAssets(logger, deleted);
                }
                if (deletedMetadata > 0)
                {
                    LogDeletedMetadata(logger, deletedMetadata);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogSweepFailure(logger, exception);
            }

            try
            {
                await Task.Delay(options.Value.CleanupInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Deleted {DrawingAssetCount} expired drawing assets")]
    private static partial void LogDeletedAssets(ILogger logger, int drawingAssetCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Deleted {DrawingAssetMetadataCount} expired drawing asset metadata rows")]
    private static partial void LogDeletedMetadata(ILogger logger, int drawingAssetMetadataCount);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Drawing asset expiry sweep failed")]
    private static partial void LogSweepFailure(ILogger logger, Exception exception);
}
