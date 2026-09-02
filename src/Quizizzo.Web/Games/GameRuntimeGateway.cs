using Quizizzo.Application.Abstractions;
using Quizizzo.GameContracts;
using Quizizzo.GameEngine;
using System.Text.Json;

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
            request.Participants,
            request.Configuration), cancellationToken);
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
        var action = DecodeAction(command);
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
        var response = new RuntimeGameCommandResult(
            result.Outcome == GameCommandOutcome.Applied,
            result.IsDuplicate,
            result.Phase,
            result.PhaseEndsAtUtc,
            status.IsComplete,
            result.ErrorCode,
            result.ErrorMessage,
            view.Scores);
        if (status.IsComplete)
        {
            await runtime.ReleaseAsync(command.GameInstanceId);
        }
        return response;
    }

    private IGameAction DecodeAction(RuntimeGameCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ActionKind) || command.ActionKind.Length > 128)
        {
            return new InvalidGameAction(
                command.ActionKind ?? string.Empty,
                "invalid-action",
                "A bounded game action kind is required.");
        }
        try
        {
            return modules.DecodeAction(command.GameKey, command.ActionKind, command.Payload);
        }
        catch (GameRuleViolationException exception)
        {
            return new InvalidGameAction(command.ActionKind, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return new InvalidGameAction(
                command.ActionKind,
                "invalid-payload",
                "The game action payload is invalid.");
        }
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
        var response = new RuntimeGameView(
            view.GameInstanceId,
            view.GameKey,
            view.Role,
            view.Phase,
            view.Revision,
            view.PhaseEndsAtUtc,
            view.IsComplete,
            view.Data,
            view.Scores);
        if (view.IsComplete)
        {
            await runtime.ReleaseAsync(gameInstanceId);
        }
        return response;
    }
}
