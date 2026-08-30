using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Quizizzo.Application.Parties;
using Quizizzo.Application.Players;
using Quizizzo.Domain.Players;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Endpoints;

public static class PlayerSessionEndpoints
{
    private const long MaximumJoinRequestBytes = 8 * 1024;
    public const string PlayerCookieName = "quizizzo.player";
    public const string PlayerContextItem = "Quizizzo.PlayerSession";

    public static IEndpointRouteBuilder MapPlayerSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/join-player", JoinPlayerAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumJoinRequestBytes))
            .RequireRateLimiting("player-join");
        return endpoints;
    }

    private static async Task<IResult> JoinPlayerAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        PlayerService players,
        IPartyRealtimeNotifier notifier,
        IWebHostEnvironment environment)
    {
        if (context.Request.ContentLength is > MaximumJoinRequestBytes ||
            !context.Request.HasFormContentType)
        {
            return Results.BadRequest("A bounded join form is required.");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var roomCode = form["roomCode"].ToString();
            var displayName = form["displayName"].ToString();
            var character = new CharacterSelection(
                ParseChoice<CharacterPresentation>(form["presentation"].ToString()),
                ParseChoice<CharacterSkinTone>(form["skinTone"].ToString()),
                ParseChoice<CharacterHairColour>(form["hairColour"].ToString()),
                ParseChoice<CharacterShirtColour>(form["shirtColour"].ToString()),
                ParseChoice<CharacterTrouserColour>(form["trouserColour"].ToString()),
                ParseChoice<CharacterTrouserLength>(form["trouserLength"].ToString()),
                ParseChoice<CharacterShoeColour>(form["shoeColour"].ToString()),
                ParseChoiceOrDefault(form["hairStyle"].ToString(), CharacterHairStyle.Style1),
                ParseChoiceOrDefault(form["eyeColour"].ToString(), CharacterEyeColour.Blue),
                ParseChoiceOrDefault(form["eyeSize"].ToString(), CharacterEyeSize.Large),
                ParseChoiceOrDefault(form["faceShape"].ToString(), CharacterFaceShape.Round),
                ParseChoiceOrDefault(form["noseShape"].ToString(), CharacterNoseShape.Nose1),
                ParseChoiceOrDefault(form["mouth"].ToString(), CharacterMouth.Smile),
                ParseChoiceOrDefault(form["browShape"].ToString(), CharacterBrowShape.Brow1),
                ParseChoiceOrDefault(form["shoeStyle"].ToString(), CharacterShoeStyle.Style1),
                ParseChoiceOrDefault(form["shirtStyle"].ToString(), CharacterShirtStyle.Default),
                ParseChoiceOrDefault(form["trouserStyle"].ToString(), CharacterTrouserStyle.Style1),
                ParseChoiceOrDefault(form["bodySize"].ToString(), CharacterBodySize.Normal));
            context.Request.Cookies.TryGetValue(PlayerCookieName, out var existingSessionToken);

            var joined = await players.JoinAsync(
                roomCode,
                displayName,
                existingSessionToken,
                character,
                context.RequestAborted);

            context.Response.Cookies.Append(PlayerCookieName, joined.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(90),
                SameSite = SameSiteMode.Lax,
                Secure = !environment.IsDevelopment() || context.Request.IsHttps
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

    private static T ParseChoice<T>(string value) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(
            option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
        if (name is null || !Enum.TryParse<T>(name, out var parsed))
        {
            throw new ArgumentException("Choose a valid character option.");
        }
        return parsed;
    }

    private static T ParseChoiceOrDefault<T>(string value, T defaultValue) where T : struct, Enum
        => string.IsNullOrWhiteSpace(value) ? defaultValue : ParseChoice<T>(value);
}
