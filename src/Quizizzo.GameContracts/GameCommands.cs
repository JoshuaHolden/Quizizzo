namespace Quizizzo.GameContracts;

public interface IGameAction
{
    string Kind { get; }
}

public sealed record InvalidGameAction(
    string OriginalKind,
    string ErrorCode,
    string ErrorMessage) : IGameAction
{
    public string Kind => OriginalKind;
}

public sealed record DeadlineElapsedAction(DateTimeOffset ScheduledForUtc) : IGameAction
{
    public string Kind => "engine.deadline-elapsed";
}

public sealed record SimulationTickElapsedAction(DateTimeOffset ScheduledForUtc) : IGameAction
{
    public string Kind => "engine.simulation-tick-elapsed";
}

public sealed record PlayerPresenceChangedAction(Guid PlayerId, bool IsConnected) : IGameAction
{
    public string Kind => "engine.player-presence-changed";
}

public enum GameActorRole
{
    Host,
    Player,
    System
}

public sealed record GameActor(GameActorRole Role, string SubjectId)
{
    public static GameActor Host(string hostUserId) => new(GameActorRole.Host, hostUserId);
    public static GameActor Player(Guid playerId) => new(GameActorRole.Player, playerId.ToString("N"));
    public static GameActor SystemActor { get; } = new(GameActorRole.System, "engine");

    public bool TryGetPlayerId(out Guid playerId)
    {
        playerId = default;
        return Role == GameActorRole.Player && Guid.TryParse(SubjectId, out playerId);
    }
}

public sealed record GameActionContext(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    GameActor Actor,
    DateTimeOffset ReceivedAtUtc);
