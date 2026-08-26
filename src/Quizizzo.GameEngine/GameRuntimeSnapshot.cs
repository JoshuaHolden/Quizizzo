using Quizizzo.GameContracts;

namespace Quizizzo.GameEngine;

public sealed record GameRuntimeSnapshot(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string HostUserId,
    string GameKey,
    IReadOnlyList<GameParticipant> Participants,
    GameModuleState ModuleState,
    IReadOnlyDictionary<Guid, int> Scores,
    IReadOnlyDictionary<GameCommandId, GameCommandResult> ProcessedCommands,
    long Revision,
    DateTimeOffset UpdatedAtUtc);

public sealed record GameSessionStatus(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string GameKey,
    string Phase,
    long Revision,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete);

public sealed record GameViewRequest(GameAudienceRole Role, string SubjectId)
{
    public static GameViewRequest Host(string hostUserId) => new(GameAudienceRole.Host, hostUserId);
    public static GameViewRequest Display(string displaySessionId) => new(GameAudienceRole.Display, displaySessionId);
    public static GameViewRequest Player(Guid playerId) => new(GameAudienceRole.Player, playerId.ToString("N"));
}

public sealed record GameRoleView(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string GameKey,
    GameAudienceRole Role,
    Guid? PlayerId,
    string Phase,
    long Revision,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    System.Text.Json.JsonElement Data,
    IReadOnlyDictionary<Guid, int> Scores);
