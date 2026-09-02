using System.Text.Json;
using Quizizzo.Application.Abstractions;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Displays;
using Quizizzo.Domain.Parties;
using Quizizzo.Domain.Players;
using Quizizzo.GameContracts;

namespace Quizizzo.Application.Games;

public sealed class PartyGameService(
    IPartyRepository parties,
    IPlayerRepository players,
    IDisplaySessionRepository displays,
    IPartyGameRuntime runtime,
    PartyMutationCoordinator partyMutations,
    TimeProvider timeProvider)
{
    public IReadOnlyList<GameDescriptor> ListGames() => runtime.ListGames();

    public Task<PartyGameSessionView> StartAsync(
        Guid partyId,
        string hostUserId,
        string gameKey,
        CancellationToken cancellationToken = default)
        => StartAsync(
            partyId, hostUserId, gameKey, GameJson.Empty, cancellationToken);

    public async Task<PartyGameSessionView> StartAsync(
        Guid partyId,
        string hostUserId,
        string gameKey,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        await using var mutation = await partyMutations.AcquireAsync(
            new PartyId(partyId), cancellationToken);
        var party = await GetOwnedPartyAsync(partyId, hostUserId, cancellationToken);
        if (party.Status != PartyStatus.Lobby || party.CurrentGameInstanceId.HasValue)
        {
            throw new InvalidOperationException("A new game can start only from the party lobby.");
        }

        var members = await players.ListMembersAsync(party.Id, cancellationToken);
        var gameInstanceId = GameInstanceId.New();
        var participants = members.Select(player => new GameParticipant(
            player.Id.Value,
            player.DisplayName.Value,
            player.Score)).ToArray();
        var status = await runtime.StartAsync(new RuntimeGameStart(
            gameInstanceId,
            party.Id.Value,
            hostUserId,
            gameKey,
            participants,
            configuration), cancellationToken);

        party.StartGame(gameInstanceId.Value, gameKey, timeProvider.GetUtcNow());
        await parties.SaveChangesAsync(cancellationToken);
        return new PartyGameSessionView(
            party.Id.Value,
            status.GameInstanceId.Value,
            gameKey,
            status.Phase,
            status.PhaseEndsAtUtc,
            status.IsComplete);
    }

    public async Task<PartyGameView?> GetHostViewAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        var party = await GetOwnedPartyAsync(partyId, hostUserId, cancellationToken);
        return await GetViewAsync(party, GameAudienceRole.Host, hostUserId, cancellationToken);
    }

    public async Task<PartyGameView?> GetDisplayViewAsync(
        Guid partyId,
        string displaySessionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(displaySessionId, out var parsedDisplaySessionId))
        {
            throw new UnauthorizedAccessException("A valid paired display identity is required.");
        }
        var display = await displays.GetByIdAsync(
            new DisplaySessionId(parsedDisplaySessionId), cancellationToken);
        var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        if (display?.PartyId != party.Id)
        {
            throw new UnauthorizedAccessException("This display is not paired to the party.");
        }
        return await GetViewAsync(party, GameAudienceRole.Display, displaySessionId, cancellationToken);
    }

    public async Task<PartyGameView?> GetPlayerViewAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await players.GetByIdAsync(new PlayerId(playerId), cancellationToken)
            ?? throw new PlayerSessionNotFoundException();
        var party = await parties.GetByIdAsync(player.PartyId, cancellationToken)
            ?? throw new PartyNotFoundException();
        return await GetViewAsync(
            party,
            GameAudienceRole.Player,
            player.Id.Value.ToString("N"),
            cancellationToken);
    }

    public async Task<PartyGameCommandView> ExecuteHostActionAsync(
        Guid partyId,
        string hostUserId,
        Guid commandId,
        string actionKind,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var party = await GetOwnedPartyAsync(partyId, hostUserId, cancellationToken);
        return await ExecuteAsync(
            party,
            GameActor.Host(hostUserId),
            commandId,
            actionKind,
            payload,
            cancellationToken);
    }

    public async Task<PartyGameCommandView> ExecutePlayerActionAsync(
        Guid playerId,
        Guid commandId,
        string actionKind,
        JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        var player = await players.GetByIdAsync(new PlayerId(playerId), cancellationToken)
            ?? throw new PlayerSessionNotFoundException();
        var party = await parties.GetByIdAsync(player.PartyId, cancellationToken)
            ?? throw new PartyNotFoundException();
        return await ExecuteAsync(
            party,
            GameActor.Player(player.Id.Value),
            commandId,
            actionKind,
            payload,
            cancellationToken);
    }

    private async Task<PartyGameCommandView> ExecuteAsync(
        Party party,
        GameActor actor,
        Guid commandId,
        string actionKind,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (party.Status != PartyStatus.Playing ||
            party.CurrentGameInstanceId is not { } instanceId ||
            string.IsNullOrWhiteSpace(party.CurrentGameKey))
        {
            throw new InvalidOperationException("This party does not have an active game.");
        }

        var result = await runtime.ExecuteAsync(new RuntimeGameCommand(
            new GameCommandId(commandId),
            new GameInstanceId(instanceId),
            party.Id.Value,
            party.CurrentGameKey,
            actor,
            actionKind,
            payload), cancellationToken);

        if (result.Applied && result.IsComplete)
        {
            await FinalizeGameAsync(party, instanceId, result.Scores, cancellationToken);
        }

        return new PartyGameCommandView(
            result.Applied,
            result.IsDuplicate,
            result.Phase,
            result.PhaseEndsAtUtc,
            result.IsComplete,
            result.ErrorCode,
            result.ErrorMessage);
    }

    private async Task<PartyGameView?> GetViewAsync(
        Party party,
        GameAudienceRole role,
        string subjectId,
        CancellationToken cancellationToken)
    {
        if (party.Status != PartyStatus.Playing ||
            party.CurrentGameInstanceId is not { } instanceId)
        {
            return null;
        }

        var view = await runtime.GetViewAsync(
            new GameInstanceId(instanceId), role, subjectId, cancellationToken);
        if (view.IsComplete)
        {
            await FinalizeGameAsync(party, instanceId, view.Scores, cancellationToken);
            return null;
        }
        return new PartyGameView(
            party.Id.Value,
            view.GameInstanceId.Value,
            view.GameKey,
            view.Role,
            view.Phase,
            view.Revision,
            view.PhaseEndsAtUtc,
            view.IsComplete,
            view.Data,
            view.Scores);
    }

    private async Task FinalizeGameAsync(
        Party party,
        Guid instanceId,
        IReadOnlyDictionary<Guid, int> scores,
        CancellationToken cancellationToken)
    {
        if (party.CurrentGameInstanceId != instanceId)
        {
            return;
        }

        var members = await players.ListMembersAsync(party.Id, cancellationToken);
        foreach (var player in members)
        {
            if (scores.TryGetValue(player.Id.Value, out var score))
            {
                player.SetScore(score);
            }
        }
        party.ReturnToLobby(instanceId);
        await players.SaveChangesAsync(cancellationToken);
        await parties.SaveChangesAsync(cancellationToken);
    }

    private async Task<Party> GetOwnedPartyAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(new PartyId(partyId), cancellationToken)
            ?? throw new PartyNotFoundException();
        if (!party.IsOwnedBy(hostUserId))
        {
            throw new PartyAccessDeniedException();
        }
        return party;
    }
}

public sealed record PartyGameSessionView(
    Guid PartyId,
    Guid GameInstanceId,
    string GameKey,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete);

public sealed record PartyGameView(
    Guid PartyId,
    Guid GameInstanceId,
    string GameKey,
    GameAudienceRole Role,
    string Phase,
    long Revision,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    JsonElement Data,
    IReadOnlyDictionary<Guid, int> Scores);

public sealed record PartyGameCommandView(
    bool Applied,
    bool IsDuplicate,
    string Phase,
    DateTimeOffset? PhaseEndsAtUtc,
    bool IsComplete,
    string? ErrorCode,
    string? ErrorMessage);
