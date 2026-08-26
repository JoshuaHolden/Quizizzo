using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

public sealed record GameCommand(
    GameCommandId CommandId,
    GameInstanceId GameInstanceId,
    Guid PartyId,
    GameActor Actor,
    IGameAction Action);

public enum GameCommandOutcome
{
    Applied,
    Rejected
}

public sealed record GameCommandResult(
    GameCommandId CommandId,
    GameCommandOutcome Outcome,
    bool IsDuplicate,
    long Revision,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    IReadOnlyList<ScoreAward> ScoreAwards,
    IReadOnlyList<GameEvent> Events,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record GameStartRequest(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string HostUserId,
    string GameKey,
    IReadOnlyList<GameParticipant> Participants);
