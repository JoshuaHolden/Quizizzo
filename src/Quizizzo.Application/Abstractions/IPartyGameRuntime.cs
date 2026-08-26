using System.Text.Json;
using Quizizzo.GameContracts;

namespace Quizizzo.Application.Abstractions;

public interface IPartyGameRuntime
{
    IReadOnlyList<GameDescriptor> ListGames();

    Task<RuntimeGameStatus> StartAsync(
        RuntimeGameStart request,
        CancellationToken cancellationToken = default);

    Task<RuntimeGameCommandResult> ExecuteAsync(
        RuntimeGameCommand command,
        CancellationToken cancellationToken = default);

    Task<RuntimeGameView> GetViewAsync(
        GameInstanceId gameInstanceId,
        GameAudienceRole role,
        string subjectId,
        CancellationToken cancellationToken = default);
}

public sealed record RuntimeGameStart(
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string HostUserId,
    string GameKey,
    IReadOnlyList<GameParticipant> Participants);

public sealed record RuntimeGameStatus(
    GameInstanceId GameInstanceId,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete);

public sealed record RuntimeGameCommand(
    GameCommandId CommandId,
    GameInstanceId GameInstanceId,
    Guid PartyId,
    string GameKey,
    GameActor Actor,
    string ActionKind,
    JsonElement Payload);

public sealed record RuntimeGameCommandResult(
    bool Applied,
    bool IsDuplicate,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyDictionary<Guid, int> Scores);

public sealed record RuntimeGameView(
    GameInstanceId GameInstanceId,
    string GameKey,
    GameAudienceRole Role,
    string Phase,
    long Revision,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    JsonElement Data,
    IReadOnlyDictionary<Guid, int> Scores);
