using Microsoft.AspNetCore.Antiforgery;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Endpoints;

public static class PlayerSessionEndpoints
{
    public const string PlayerCookieName = "quizizzo.player";
    public const string PlayerContextItem = "Quizizzo.PlayerSession";

    public static IEndpointRouteBuilder MapPlayerSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/join-player", JoinPlayerAsync)
            .RequireRateLimiting("player-join");
        return endpoints;
    }

    private static async Task<IResult> JoinPlayerAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        PlayerService players,
        IPartyRealtimeNotifier notifier)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var roomCode = form["roomCode"].ToString();
            var displayName = form["displayName"].ToString();
            context.Request.Cookies.TryGetValue(PlayerCookieName, out var existingSessionToken);

            var joined = await players.JoinAsync(
                roomCode,
                displayName,
                existingSessionToken,
                context.RequestAborted);

            context.Response.Cookies.Append(PlayerCookieName, joined.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(90),
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps
            });

            await notifier.PartyChangedAsync(
                joined.View.PartyId,
                joined.IsNew ? "PlayerJoined" : "PlayerReconnected",
                context.RequestAborted);

            return Results.Redirect("/play");
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("The join form expired. Go back and try again.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or PartyNotFoundException)
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var roomCode = Uri.EscapeDataString(form["roomCode"].ToString());
            var error = Uri.EscapeDataString(exception.Message);
            return Results.Redirect($"/join/{roomCode}?error={error}");
        }
    }
}
