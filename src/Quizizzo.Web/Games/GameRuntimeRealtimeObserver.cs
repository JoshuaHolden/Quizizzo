using Quizizzo.GameEngine;
using Quizizzo.GameContracts;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Games;

public sealed partial class GameRuntimeRealtimeObserver(
    IPartyRealtimeNotifier notifier,
    ILogger<GameRuntimeRealtimeObserver> logger) : IGameRuntimeObserver
{
    public async Task StateChangedAsync(
        GameRuntimeChange change,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await notifier.PartyChangedAsync(
                change.PartyId,
                $"GameStateChanged:{change.Result.Phase}",
                cancellationToken);
        }
        catch (Exception exception)
        {
            LogPublishFailure(logger, exception, change.PartyId, change.GameInstanceId);
        }
    }

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Error,
        Message = "Failed to publish game state change for party {PartyId} and game {GameInstanceId}")]
    private static partial void LogPublishFailure(
        ILogger logger,
        Exception exception,
        Guid partyId,
        GameInstanceId gameInstanceId);
}
