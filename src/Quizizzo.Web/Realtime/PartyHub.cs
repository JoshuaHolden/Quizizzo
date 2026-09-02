using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Application.Games;
using Quizizzo.GameContracts;
using Quizizzo.Web.Endpoints;

namespace Quizizzo.Web.Realtime;

public sealed class PartyHub(
    PartyService parties,
    PlayerService players,
    DisplaySessionService displays,
    PartyGameService games,
    PartyConnectionRegistry connections,
    PlayerReactionLimiter reactionLimiter,
    TimeProvider timeProvider) : Hub<IPartyClient>
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

    public async Task<PartyGameCommandView> SubmitPlayerAction(
        Guid commandId,
        string actionKind,
        JsonElement payload)
    {
        var token = GetCookie(PlayerSessionEndpoints.PlayerCookieName);
        var player = await players.ReconnectAsync(token, Context.ConnectionAborted);
        var currentView = await games.GetPlayerViewAsync(player.PlayerId, Context.ConnectionAborted);
        var playerView = currentView?.Data.Deserialize<PlayerGameViewPayload>();
        if (playerView?.Controller.Kind == PlayerControllerKind.Drawing &&
            string.Equals(playerView.Controller.ActionKind, actionKind, StringComparison.Ordinal))
        {
            throw new HubException("Drawing submissions must use the validated asset upload endpoint.");
        }
        var result = await games.ExecutePlayerActionAsync(
            player.PlayerId,
            commandId,
            actionKind,
            payload,
            Context.ConnectionAborted);
        if (!result.Applied)
        {
            throw new HubException(result.ErrorMessage ?? "The game action was rejected.");
        }
        return result;
    }

    public async Task SendReaction(string reaction)
    {
        var allowed = new[]
        {
            "Kiss", "Angry", "Laugh", "Wow", "Poop", "Fake", "Unsubscribe", "Report"
        };
        var selected = allowed.FirstOrDefault(value =>
            string.Equals(value, reaction, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            throw new HubException("That reaction is not available.");
        }

        var token = GetCookie(PlayerSessionEndpoints.PlayerCookieName);
        var player = await players.ReconnectAsync(token, Context.ConnectionAborted);
        if (!reactionLimiter.TryAcquire(player.PlayerId))
        {
            throw new HubException("Wait a moment before reacting again.");
        }

        await Clients.Group(RealtimeGroups.Displays(player.PartyId)).PlayerReacted(
            new PlayerReactionMessage(player.PlayerId, selected, timeProvider.GetUtcNow()));
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
