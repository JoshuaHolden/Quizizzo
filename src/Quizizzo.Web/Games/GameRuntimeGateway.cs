using Quizizzo.Application.Abstractions;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;

namespace Quizizzo.Web.Games;

public sealed class GameRuntimeGateway(
    GameRuntimeManager runtime,
    GameModuleCatalog modules) : IPartyGameRuntime
{
    public IReadOnlyList<GameDescriptor> ListGames() => runtime.ListGames();

    public async Task<RuntimeGameStatus> StartAsync(
        RuntimeGameStart request,
        CancellationToken cancellationToken = default)
    {
        var status = await runtime.StartAsync(new GameStartRequest(
            request.GameInstanceId,
            request.PartyId,
            request.HostUserId,
            request.GameKey,
            request.Participants), cancellationToken);
        return new RuntimeGameStatus(
            status.GameInstanceId,
            status.Phase,
            status.PhaseEndsAtUtc,
            status.IsComplete);
    }

    public async Task<RuntimeGameCommandResult> ExecuteAsync(
        RuntimeGameCommand command,
        CancellationToken cancellationToken = default)
    {
        var action = modules.DecodeAction(command.GameKey, command.ActionKind, command.Payload);
        var result = await runtime.ExecuteAsync(new GameCommand(
            command.CommandId,
            command.GameInstanceId,
            command.PartyId,
            command.Actor,
            action), cancellationToken);
        var status = await runtime.GetStatusAsync(command.GameInstanceId, cancellationToken);
        var view = await runtime.GetViewAsync(
            command.GameInstanceId,
            command.Actor.Role == GameActorRole.Host
                ? GameViewRequest.Host(command.Actor.SubjectId)
                : GameViewRequest.Player(Guid.Parse(command.Actor.SubjectId)),
            cancellationToken);
        return new RuntimeGameCommandResult(
            result.Outcome == GameCommandOutcome.Applied,
            result.IsDuplicate,
            result.Phase,
            result.PhaseEndsAtUtc,
            status.IsComplete,
            result.ErrorCode,
            result.ErrorMessage,
            view.Scores);
    }

    public async Task<RuntimeGameView> GetViewAsync(
        GameInstanceId gameInstanceId,
        GameAudienceRole role,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var request = role switch
        {
            GameAudienceRole.Host => GameViewRequest.Host(subjectId),
            GameAudienceRole.Display => GameViewRequest.Display(subjectId),
            GameAudienceRole.Player => GameViewRequest.Player(Guid.Parse(subjectId)),
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
        var view = await runtime.GetViewAsync(gameInstanceId, request, cancellationToken);
        return new RuntimeGameView(
            view.GameInstanceId,
            view.GameKey,
            view.Role,
            view.Phase,
            view.Revision,
            view.PhaseEndsAtUtc,
            view.IsComplete,
            view.Data,
            view.Scores);
    }
}
