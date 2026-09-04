using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quizizzo.Application.Abstractions;

namespace Quizizzo.Infrastructure.Voice;

public sealed partial class VoiceSampleCleanupService(
    IVoiceSampleStore sampleStore,
    IServiceScopeFactory scopeFactory,
    IOptions<VoiceSampleStoreOptions> options,
    TimeProvider timeProvider,
    ILogger<VoiceSampleCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                await sampleStore.DeleteExpiredAsync(now.Subtract(options.Value.RetentionPeriod), stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var metadata = scope.ServiceProvider.GetRequiredService<IVoiceSampleMetadataRepository>();
                await metadata.DeleteExpiredAsync(now, stoppingToken);
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

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "Voice sample expiry sweep failed")]
    private static partial void LogSweepFailure(ILogger logger, Exception exception);
}