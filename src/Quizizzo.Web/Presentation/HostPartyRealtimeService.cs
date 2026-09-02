using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Games;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.GameContracts;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Presentation;

public sealed class HostPartyRealtimeService(
    IServiceScopeFactory scopeFactory,
    PartyConnectionRegistry connections,
    IPartyRealtimeNotifier realtime)
{
    public IReadOnlyList<GameDescriptor> ListGames()
    {
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PartyGameService>().ListGames();
    }

    public async Task<HostPartyRealtimeState> LoadAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var parties = services.GetRequiredService<PartyService>();
        var players = services.GetRequiredService<PlayerService>();
        var games = services.GetRequiredService<PartyGameService>();

        var party = await parties.GetOwnedAsync(partyId, hostUserId, cancellationToken);
        var roster = await players.ListForHostAsync(partyId, hostUserId, cancellationToken);
        var game = await games.GetHostViewAsync(partyId, hostUserId, cancellationToken);
        return new HostPartyRealtimeState(
            party,
            roster,
            connections.GetSnapshot(partyId),
            game);
    }

    public async Task StartGameAsync(
        Guid partyId,
        string hostUserId,
        string gameKey,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PartyGameService>()
                .StartAsync(partyId, hostUserId, gameKey, configuration, cancellationToken);
        }

        await realtime.PartyChangedAsync(partyId, "GameStarted", cancellationToken);
    }

    public async Task AdvanceGameAsync(
        Guid partyId,
        string hostUserId,
        string actionKind,
        CancellationToken cancellationToken = default)
    {
        PartyGameCommandView result;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<PartyGameService>()
                .ExecuteHostActionAsync(
                    partyId,
                    hostUserId,
                    Guid.NewGuid(),
                    actionKind,
                    GameJson.Empty,
                    cancellationToken);
        }

        if (!result.Applied)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "The action was rejected.");
        }

        await realtime.PartyChangedAsync(
            partyId,
            result.IsComplete ? "GameCompleted" : "GameAdvanced",
            cancellationToken);
    }

    public async Task CloseLobbyAsync(
        Guid partyId,
        string hostUserId,
        CancellationToken cancellationToken = default)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PartyService>()
                .CloseLobbyAsync(partyId, hostUserId, cancellationToken);
        }

        await realtime.PartyChangedAsync(partyId, "LobbyClosed", cancellationToken);
    }

    public async Task KickPlayerAsync(
        Guid partyId,
        string hostUserId,
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlayerService>()
                .KickAsync(partyId, hostUserId, playerId, cancellationToken);
        }

        await realtime.PartyChangedAsync(partyId, "PlayerRemoved", cancellationToken);
    }
}

public sealed record HostPartyRealtimeState(
    PartyView Party,
    IReadOnlyList<PlayerView> Players,
    PartyPresenceSnapshot Presence,
    PartyGameView? Game);
