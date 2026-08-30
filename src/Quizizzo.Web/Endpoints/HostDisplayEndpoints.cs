using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Quizizzo.Application.Displays;
using Quizizzo.Application.Parties;
using Quizizzo.Web.Realtime;

namespace Quizizzo.Web.Endpoints;

public static class HostDisplayEndpoints
{
    private const long MaximumRequestBytes = 8 * 1024;
    public const string DisplayCookieName = "quizizzo.display";

    public static IEndpointRouteBuilder MapHostDisplayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/host/party/{partyId:guid}/present", PresentAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes))
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> PresentAsync(
        Guid partyId,
        HttpContext context,
        IAntiforgery antiforgery,
        DisplaySessionService displays,
        IPartyRealtimeNotifier notifier,
        IWebHostEnvironment environment)
    {
        if (context.Request.ContentLength is > MaximumRequestBytes ||
            !context.Request.HasFormContentType)
        {
            return Results.BadRequest("A bounded presentation form is required.");
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var hostUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new PartyAccessDeniedException();

            context.Request.Cookies.TryGetValue(DisplayCookieName, out var sessionToken);
            var restored = await displays.RestoreOrCreateAsync(sessionToken, context.RequestAborted);
            if (restored.View.PartyId == partyId)
            {
                return Results.Redirect("/display");
            }
            if (restored.View.PartyId is { } existingPartyId && existingPartyId != partyId)
            {
                restored = await displays.CreateAsync(context.RequestAborted);
            }

            var paired = await displays.PairAsync(
                restored.View.PairingCode,
                partyId,
                hostUserId,
                context.RequestAborted);

            if (restored.IsNew)
            {
                context.Response.Cookies.Append(DisplayCookieName, restored.SessionToken, new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    MaxAge = TimeSpan.FromDays(30),
                    SameSite = SameSiteMode.Lax,
                    Secure = !environment.IsDevelopment() || context.Request.IsHttps
                });
            }

            await notifier.DisplaySessionChangedAsync(
                paired.DisplaySessionId,
                "DisplayPaired",
                context.RequestAborted);
            await notifier.PartyChangedAsync(partyId, "DisplayPaired", context.RequestAborted);
            return Results.Redirect("/display");
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("The presentation request expired. Go back and try again.");
        }
        catch (PartyAccessDeniedException)
        {
            return Results.Forbid();
        }
        catch (PartyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
