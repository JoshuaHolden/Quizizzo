using Quizizzo.GameEngine;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Games;

public sealed class GameRuntimeRealtimeObserver(
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
            logger.LogError(
                exception,
                "Failed to publish game state change for party {PartyId} and game {GameInstanceId}",
                change.PartyId,
                change.GameInstanceId);
        }
    }
}
