using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Web.Endpoints;

namespace Quizizzo.Web.Realtime;

public sealed class PartyHub(
    PartyService parties,
    PlayerService players,
    DisplaySessionService displays,
    PartyConnectionRegistry connections) : Hub<IPartyClient>
{
    public async Task ConnectHost(Guid partyId)
    {
        var hostUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(hostUserId))
        {
            throw new HubException("Host authentication is required.");
        }

        await parties.GetOwnedAsync(partyId, hostUserId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Party(partyId));
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Hosts(partyId));
        await connections.RegisterAsync(
            Context.ConnectionId, partyId, RealtimeRole.Host, hostUserId, Context.ConnectionAborted);
    }

    public async Task ConnectPlayer()
    {
        var token = GetCookie(PlayerSessionEndpoints.PlayerCookieName);
        var player = await players.ReconnectAsync(token, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Party(player.PartyId));
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Players(player.PartyId));
        await connections.RegisterAsync(
            Context.ConnectionId, player.PartyId, RealtimeRole.Player, player.PlayerId.ToString(), Context.ConnectionAborted);
    }

    public async Task ConnectDisplay()
    {
        var token = GetCookie("quizizzo.display");
        var display = await displays.ReconnectAsync(token, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.DisplaySession(display.DisplaySessionId));
        if (display.PartyId is { } partyId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Party(partyId));
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Displays(partyId));
        }

        await connections.RegisterAsync(
            Context.ConnectionId,
            display.PartyId,
            RealtimeRole.Display,
            display.DisplaySessionId.ToString(),
            Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await connections.UnregisterAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private string GetCookie(string name) =>
        Context.GetHttpContext()?.Request.Cookies[name]
        ?? throw new HubException("A valid browser session is required.");
}
