using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quizizzo.Infrastructure.Identity;

namespace Quizizzo.Infrastructure.Games;

public sealed partial class GameSnapshotCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<GameStateStoreOptions> options,
    TimeProvider timeProvider,
    ILogger<GameSnapshotCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                await using var scope = scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var completedCutoff = now.Subtract(options.Value.CompletedSnapshotRetention);
                var deletedCompleted = await dbContext.GameRuntimeSnapshots
                    .Where(snapshot => snapshot.IsComplete && snapshot.UpdatedAtUtc <= completedCutoff)
                    .ExecuteDeleteAsync(stoppingToken);
                var orphanCutoff = now.Subtract(options.Value.OrphanSnapshotRetention);
                var deletedOrphans = await dbContext.GameRuntimeSnapshots
                    .Where(snapshot => !snapshot.IsComplete && snapshot.UpdatedAtUtc <= orphanCutoff &&
                        !dbContext.Parties.Any(party =>
                            party.CurrentGameInstanceId == snapshot.GameInstanceId))
                    .ExecuteDeleteAsync(stoppingToken);
                if (deletedCompleted > 0 || deletedOrphans > 0)
                {
                    LogDeletedSnapshots(
                        logger,
                        deletedCompleted,
                        deletedOrphans);
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
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Deleted {CompletedGameSnapshots} completed and {OrphanGameSnapshots} orphan game snapshots")]
    private static partial void LogDeletedSnapshots(
        ILogger logger,
        int completedGameSnapshots,
        int orphanGameSnapshots);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Game snapshot expiry sweep failed")]
    private static partial void LogSweepFailure(ILogger logger, Exception exception);
}
