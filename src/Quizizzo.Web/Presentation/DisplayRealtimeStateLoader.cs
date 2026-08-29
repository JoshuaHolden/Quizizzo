using Microsoft.Extensions.DependencyInjection;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Games;
using Quizizzo.Application.Players;

namespace Quizizzo.Web.Presentation;

public sealed class DisplayRealtimeStateLoader(IServiceScopeFactory scopeFactory)
{
    public async Task<DisplayRealtimeState> LoadAsync(
        Guid displaySessionId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var displaySessions = services.GetRequiredService<DisplaySessionService>();
        var players = services.GetRequiredService<PlayerService>();
        var games = services.GetRequiredService<PartyGameService>();

        var session = await displaySessions.GetByIdAsync(displaySessionId, cancellationToken);
        if (session.PartyId is not { } partyId)
        {
            return new DisplayRealtimeState(session, [], null);
        }

        var roster = await players.ListForDisplayAsync(partyId, cancellationToken);
        var game = await games.GetDisplayViewAsync(
            partyId,
            session.DisplaySessionId.ToString("N"),
            cancellationToken);
        return new DisplayRealtimeState(session, roster, game);
    }
}

public sealed record DisplayRealtimeState(
    DisplaySessionView Session,
    IReadOnlyList<PlayerView> Players,
    PartyGameView? Game);
