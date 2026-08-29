using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;

namespace Quizizzo.Web.Presentation;

public sealed class PlayerRealtimeStateLoader(IServiceScopeFactory scopeFactory)
{
    public async Task<PlayerRealtimeState> LoadAsync(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var players = services.GetRequiredService<PlayerService>();
        var games = services.GetRequiredService<PartyGameService>();

        var player = await players.GetByIdAsync(playerId, cancellationToken);
        var game = await games.GetPlayerViewAsync(playerId, cancellationToken);
        return new PlayerRealtimeState(player, game);
    }
}

public sealed record PlayerRealtimeState(PlayerView Player, PartyGameView? Game);
