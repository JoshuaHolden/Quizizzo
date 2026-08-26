using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

public sealed record GameRuntimeChange(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string GameKey,
    GameCommandResult Result,
    bool IsComplete,
    IReadOnlyDictionary<Guid, int> Scores);

public interface IGameRuntimeObserver
{
    Task StateChangedAsync(GameRuntimeChange change, CancellationToken cancellationToken = default);
}
